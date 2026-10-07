using System.Collections.Concurrent;
using ClearanceLink.Protocol;
using ClearanceLink.Protocol.Facilities;

namespace ClearanceLink.Server;

/// <summary>
/// One SimConnect client connection, answered from X-Plane. Transport-agnostic: feed it client packets with
/// <see cref="HandleAsync"/>, call <see cref="TickAsync"/> regularly for periodic data, and it emits replies through
/// the send delegate. Every call type has an explicit policy; anything else is logged once and otherwise ignored.
/// </summary>
public sealed class SimConnectSession(
    int connectionId,
    ServerIdentity identity,
    SimVarResolver resolver,
    IXPlaneData xplane,
    Func<Packet, Task> send,
    Action<string> log,
    IFacilitySource? facilities = null,
    TimeProvider? time = null,
    TrafficRegistry? traffic = null,
    IGroundElevation? ground = null,
    IReadOnlyList<(string Title, string Livery)>? liveries = null)
{
    /// <summary>Object id MSFS 2024 reports for the user aircraft in SimObjectData (recorded 2026-09-30).</summary>
    public const uint UserObjectId = 524288;

    /// <summary>Most distinct one-time messages a connection logs; then one line says the rest are suppressed.</summary>
    public const int MaxLoggedOnce = 1000;

    private static readonly TimeSpan FramePeriod = TimeSpan.FromMilliseconds(1000.0 / 30);
    private static readonly TimeSpan SecondPeriod = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<uint, List<Datum>> _definitions = [];
    private readonly Dictionary<uint, DataRequest> _requests = [];
    private readonly Dictionary<uint, string> _clientEvents = [];
    private readonly Dictionary<uint, FacilityDefinition> _facilityDefinitions = [];
    private FacilityEncoder? _facilityEncoder;
    private readonly HashSet<string> _loggedOnce = new(StringComparer.Ordinal);
    private bool _suppressedLogged;
    private readonly Lock _gate = new();
    private uint _protocol = 6;

    public int ConnectionId => connectionId;

    public string ClientName { get; private set; } = "";

    public SessionStats Stats { get; } = new((time ?? TimeProvider.System).GetUtcNow());

    public async Task HandleAsync(Packet packet)
    {
        if (packet.Direction != Direction.ClientToServer)
        {
            return;
        }

        Stats.Count(packet.Name, _time.GetUtcNow());
        var sendId = packet.SendId ?? 0;
        var f = PacketDecoder.Decode(packet);
        switch (packet.FunctionId)
        {
            case 0x01: // Open
                _protocol = packet.Protocol;
                ClientName = (string?)f["appName"] ?? "";
                log($"#{connectionId} Open from '{ClientName}' (protocol {_protocol})");
                await send(identity.BuildOpenReply(_protocol)).ConfigureAwait(false);
                break;

            case 0x04: // MapClientEventToSimEvent
                lock (_gate)
                {
                    _clientEvents[U(f, "eventId")] = S(f, "eventName");
                }

                break;

            case 0x05: // TransmitClientEvent
                await ApplyEventAsync(U(f, "objectId"), U(f, "eventId"), U(f, "data")).ConfigureAwait(false);
                break;

            case 0x44: // TransmitClientEvent_EX1: objectId, eventId, groupId, flags, data0..4
                var ex1 = new PayloadReader(packet.Payload);
                var objectId = ex1.U32();
                var eventId = ex1.U32();
                ex1.U32();
                ex1.U32();
                await ApplyEventAsync(objectId, eventId, ex1.U32()).ConfigureAwait(false);
                break;

            case 0x0C: // AddToDataDefinition
                var datum = new Datum(
                    S(f, "datumName"),
                    S(f, "unitsName"),
                    (SimConnectDataType)U(f, "dataType"),
                    (float)f["epsilon"]!,
                    U(f, "datumId"));
                lock (_gate)
                {
                    if (!_definitions.TryGetValue(U(f, "defineId"), out var list))
                    {
                        _definitions[U(f, "defineId")] = list = [];
                    }

                    list.Add(datum);
                }

                if (!resolver.Prepare(datum.Name) && !DataEncoder.IsText(datum.Type))
                {
                    Stats.Unmapped(datum.Name);
                    LogOnce($"unmapped:{datum.Name}", $"#{connectionId} UNMAPPED SimVar '{datum.Name}' ({datum.Units}); answering 0");
                }

                break;

            case 0x0D: // ClearDataDefinition
                lock (_gate)
                {
                    _definitions.Remove(U(f, "defineId"));
                }

                break;

            case 0x0E: // RequestDataOnSimObject
                await RequestDataAsync(
                    new DataRequest(
                        U(f, "requestId"),
                        U(f, "defineId"),
                        U(f, "objectId"),
                        (SimConnectPeriod)U(f, "period"),
                        (DataRequestFlags)U(f, "flags"),
                        U(f, "interval"),
                        U(f, "limit")),
                    sendId).ConfigureAwait(false);
                break;

            case 0x0F: // RequestDataOnSimObjectType: only the user aircraft exists on our side
                await SendDataAsync(
                    RecvId.SimObjectDataByType,
                    new DataRequest(U(f, "requestId"), U(f, "defineId"), 0, SimConnectPeriod.Once, DataRequestFlags.Default, 0, 0),
                    force: true).ConfigureAwait(false);
                break;

            case 0x45: // AddToFacilityDefinition
                lock (_gate)
                {
                    var defineId = U(f, "defineId");
                    if (!_facilityDefinitions.TryGetValue(defineId, out var definition))
                    {
                        _facilityDefinitions[defineId] = definition = new FacilityDefinition();
                    }

                    definition.Add(S(f, "fieldName"));
                }

                break;

            case 0x46: // RequestFacilityData
            case 0x4A: // RequestFacilityData_EX1
                await RequestFacilityAsync(S(f, "icao"), U(f, "defineId"), U(f, "requestId"), sendId).ConfigureAwait(false);
                break;

            // Without a traffic registry these stay unanswered, as in the stand-in test of 2026-09-30 where BATC ran
            // fine without them: BATC waits for the livery list before creating AI traffic, so traffic stays off.
            case 0x5B when liveries is not null: // EnumerateSimObjectsAndLiveries
                await SendLiveriesAsync(U(f, "requestId")).ConfigureAwait(false);
                break;
            case 0x27 or 0x28 or 0x29 or 0x2A or 0x57 or 0x58 or 0x59 or 0x5A when traffic is not null: // AI object creation
                await CreateObjectAsync(packet, f).ConfigureAwait(false);
                break;
            case 0x5B: // EnumerateSimObjectsAndLiveries
            case 0x27 or 0x28 or 0x29 or 0x2A or 0x57 or 0x58 or 0x59 or 0x5A: // AI object creation
                LogOnce($"ai:{packet.Name}", $"#{connectionId} {packet.Name} not answered: AI traffic is not supported yet");
                break;

            case 0x2C when traffic is not null: // AIRemoveObject
                traffic.Remove(U(f, "objectId"));
                break;

            case 0x10: // SetDataOnSimObject: AI traffic state is kept; user-aircraft writes (GSX L-vars) are not applied
                if (traffic?.Find(U(f, "objectId")) is { } target)
                {
                    ApplySetData(target, packet);
                }
                else
                {
                    LogOnce($"setdata:{f["defineId"]}", $"#{connectionId} SetDataOnSimObject define {f["defineId"]} ignored");
                }

                break;

            // Accepted without a reply: notification groups, input mapping, system-event subscriptions,
            // AI release/remove, client data setup.
            case 0x06 or 0x07 or 0x08 or 0x09 or 0x0A or 0x0B or 0x11 or 0x12 or 0x13 or 0x14 or 0x15 or 0x17 or 0x18
                or 0x2B or 0x2C or 0x37 or 0x38 or 0x39 or 0x3A or 0x4D or 0x55 or 0x56 or 0x5C or 0x5D:
                break;

            default:
                Stats.Unhandled(packet.Name);
                LogOnce($"call:{packet.Name}", $"#{connectionId} UNHANDLED call {packet.Name}; ignored");
                break;
        }
    }

    /// <summary>Sends any periodic data that is due. Call often (every frame or so).</summary>
    public async Task TickAsync()
    {
        List<DataRequest> due;
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            due = _requests.Values.Where(r => r.NextDue <= now).ToList();
            foreach (var r in due)
            {
                r.NextDue = now + (r.Period == SimConnectPeriod.Second ? SecondPeriod : FramePeriod) * (1 + r.Interval);
            }
        }

        foreach (var request in due)
        {
            await SendDataAsync(RecvId.SimObjectData, request, force: false).ConfigureAwait(false);
            if (request.Limit > 0 && ++request.Sent >= request.Limit)
            {
                lock (_gate)
                {
                    _requests.Remove(request.RequestId);
                }
            }
        }
    }

    private async Task RequestDataAsync(DataRequest request, uint sendId)
    {
        lock (_gate)
        {
            _requests.Remove(request.RequestId);
        }

        if (request.ObjectId is not (0 or UserObjectId))
        {
            if (traffic?.Find(request.ObjectId) is { } ai)
            {
                // BATC polls its AI aircraft with ONCE requests (defines 18, 19, 20) to keep them on the ground.
                if (request.Period is not (SimConnectPeriod.Once or SimConnectPeriod.Never))
                {
                    LogOnce(
                        $"ai-periodic:{request.DefineId}:{request.Period}",
                        $"#{connectionId} RequestDataOnSimObject request {request.RequestId} define {request.DefineId} period {request.Period} for AI object {request.ObjectId}: answered once; periodic AI data is not supported");
                }

                await SendTrafficDataAsync(request, ai).ConfigureAwait(false);
                return;
            }

            LogOnce($"object:{request.ObjectId}", $"#{connectionId} data requested for object {request.ObjectId}, which does not exist; not answered");
            return;
        }

        switch (request.Period)
        {
            case SimConnectPeriod.Never:
                return;
            case SimConnectPeriod.Once:
                await SendDataAsync(RecvId.SimObjectData, request, force: true).ConfigureAwait(false);
                return;
            default:
                request.NextDue = _time.GetUtcNow();
                lock (_gate)
                {
                    _requests[request.RequestId] = request;
                }

                return;
        }
    }

    private async Task SendDataAsync(RecvId recvId, DataRequest request, bool force)
    {
        List<Datum>? datums;
        lock (_gate)
        {
            datums = _definitions.GetValueOrDefault(request.DefineId)?.ToList();
        }

        if (datums is null)
        {
            LogOnce($"nodef:{request.DefineId}", $"#{connectionId} request {request.RequestId} for unknown define {request.DefineId}");
            return;
        }

        // Zeros would put the aircraft at 0,0 and BATC would load the wrong airports, so say nothing until X-Plane has
        // delivered a position. A zero clock is as bad: BeyondATC 1.8.22 reads it as year 1 and its live-traffic time
        // check overflows, stopping the flight load at 25% (issue #36). BATC keeps asking (it polls with ONCE), and
        // periodic requests retry on the next tick.
        if (!xplane.IsConnected
            || datums.Any(d => IsPosition(d.Name) && resolver.Resolve(d.Name, d.Units).Status != ResolveStatus.Ok)
            || datums.Any(d => IsClock(d.Name) && resolver.Resolve(d.Name, d.Units).Status == ResolveStatus.Pending))
        {
            LogOnce("waiting-xplane", $"#{connectionId} holding aircraft data until X-Plane is connected and reports a position and the time");
            return;
        }

        lock (_gate)
        {
            _loggedOnce.Remove("waiting-xplane");
        }

        var tagged = request.Flags.HasFlag(DataRequestFlags.Tagged);
        using var data = new MemoryStream();
        foreach (var datum in datums)
        {
            var (value, status) = resolver.Resolve(datum.Name, datum.Units, (from, to) => UnitsPassedThrough(datum.Name, from, to));
            if (status == ResolveStatus.Pending)
            {
                Stats.Pending(datum.Name);
            }

            if (status != ResolveStatus.Ok && DataEncoder.IsText(datum.Type))
            {
                value = SimValue.Of("");
            }

            if (tagged)
            {
                data.Write(BitConverter.GetBytes(datum.DatumId));
            }

            DataEncoder.Write(data, datum.Type, value);
        }

        var bytes = data.ToArray();
        if (!force && request.Flags.HasFlag(DataRequestFlags.Changed) && request.LastData is { } last && last.AsSpan().SequenceEqual(bytes))
        {
            return;
        }

        request.LastData = bytes;
        Stats.DataSent();
        await send(PacketBuilder.Server(recvId, _protocol)
            .U32(request.RequestId)
            .U32(request.ObjectId == 0 ? UserObjectId : request.ObjectId)
            .U32(request.DefineId)
            .U32((uint)request.Flags)
            .U32(1)
            .U32(1)
            .U32((uint)datums.Count)
            .Bytes(bytes)
            .Build()).ConfigureAwait(false);
    }

    private async Task ApplyEventAsync(uint objectId, uint eventId, uint data)
    {
        // BATC also sends events (lights, throttle, gear) to its AI aircraft: kept with the object for the traffic
        // plugin; only the user aircraft's events map to X-Plane.
        if (objectId is not (0 or UserObjectId))
        {
            if (traffic?.Find(objectId) is { } ai)
            {
                string? aiEvent;
                lock (_gate)
                {
                    aiEvent = _clientEvents.GetValueOrDefault(eventId);
                }

                if (aiEvent is not null)
                {
                    ai.SetEvent(aiEvent, data);
                }
            }

            return;
        }

        string? name;
        lock (_gate)
        {
            name = _clientEvents.GetValueOrDefault(eventId);
        }

        if (name is null)
        {
            LogOnce($"event-id:{eventId}", $"#{connectionId} event id {eventId} was never mapped; ignored");
            return;
        }

        Stats.Event(name);
        if (resolver.Mappings.FindEvent(name) is not { } map)
        {
            Stats.Unmapped(name);
            LogOnce($"event:{name}", $"#{connectionId} UNMAPPED event {name} (data {data}); ignored");
            return;
        }

        try
        {
            if (map.Command is { } command)
            {
                await xplane.CommandAsync(command).ConfigureAwait(false);
                log($"#{connectionId} {name} -> command {command}");
            }
            else if (map.Dataref is { } dataref)
            {
                var value = Units.Convert(unchecked((int)data), map.FromUnits, map.ToUnits, (from, to) => UnitsPassedThrough(name, from, to));
                await xplane.WriteAsync(dataref, value).ConfigureAwait(false);
                log($"#{connectionId} {name}({data}) -> {dataref} = {value}");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or HttpRequestException or OperationCanceledException)
        {
            // OperationCanceledException: HttpClient's 5 s timeout (X-Plane busy loading scenery) is a TaskCanceledException;
            // it must not reach the connection loop, which would drop BATC.
            log($"#{connectionId} {name} failed: {ex.Message}");
        }
    }

    /// <summary>
    /// EnumerateSimObjectsAndLiveries answer: SIMCONNECT_RECV_ENUMERATE_SIMOBJECT_AND_LIVERY_LIST, a list template
    /// (requestId, arraySize, entryNumber, outOf) then 512-byte entries (title[256], livery[256]). MSFS sent 79 entries
    /// per packet, 70 packets for 5,497 entries (recording 20260930-174605).
    /// </summary>
    private async Task SendLiveriesAsync(uint requestId)
    {
        const int PerPacket = 79;
        var list = liveries!;
        var packets = Math.Max(1, (list.Count + PerPacket - 1) / PerPacket);
        log($"#{connectionId} EnumerateSimObjectsAndLiveries: sending {list.Count} models in {packets} packets");
        for (var p = 0; p < packets; p++)
        {
            var chunk = list.Skip(p * PerPacket).Take(PerPacket).ToList();
            var builder = PacketBuilder.Server(RecvId.EnumerateSimObjectAndLiveryList, _protocol)
                .U32(requestId).U32((uint)chunk.Count).U32((uint)p).U32((uint)packets);
            foreach (var (title, livery) in chunk)
            {
                builder.Str(title, PacketDecoder.NameLength).Str(livery, PacketDecoder.NameLength);
            }

            await send(builder.Build()).ConfigureAwait(false);
        }
    }

    /// <summary>Any AICreate* call: register the object and answer AssignedObjectId, as MSFS does within ~15 ms.</summary>
    private async Task CreateObjectAsync(Packet packet, Dictionary<string, object?> f)
    {
        var kind = packet.FunctionId is 0x2A or 0x5A ? TrafficKind.SimulatedObject : TrafficKind.Aircraft;
        var position = f.GetValueOrDefault("initPositionHex") is string hex && hex.Length >= InitPosition.Size * 2
            ? InitPosition.Read(Convert.FromHexString(hex))
            : null;
        var created = traffic!.Create(connectionId, kind, S(f, "containerTitle"), S(f, "livery"), S(f, "tailNumber"), position);
        if (position is { } start && GroundFeet(created) is { } groundAtStart && start.AltitudeFeet - groundAtStart < 50)
        {
            created.Set("SIM ON GROUND", 1, _time.GetUtcNow()); // parked: BATC does not always set the init position's on-ground flag
        }

        Stats.Count("AI objects created", _time.GetUtcNow());
        await send(PacketBuilder.Server(RecvId.AssignedObjectId, _protocol).U32(U(f, "requestId")).U32(created.ObjectId).Build()).ConfigureAwait(false);
    }

    /// <summary>Unpacks a SetDataOnSimObject block for an AI object using the client's definition.</summary>
    private void ApplySetData(TrafficObject target, Packet packet)
    {
        var r = new PayloadReader(packet.Payload);
        var defineId = r.U32();
        r.U32(); // objectId
        var flags = r.U32();
        r.U32(); // arrayCount
        r.U32(); // unitSize
        List<Datum>? datums;
        lock (_gate)
        {
            datums = _definitions.GetValueOrDefault(defineId)?.ToList();
        }

        if (datums is null || (flags & 1) != 0)
        {
            LogOnce($"ai-setdata:{defineId}:{flags}", $"#{connectionId} SetDataOnSimObject define {defineId} (flags {flags}) for AI not understood; ignored");
            return;
        }

        var now = _time.GetUtcNow();
        foreach (var datum in datums)
        {
            if (DataEncoder.SizeOf(datum.Type) is not { } size)
            {
                return; // STRINGV: cannot know the length
            }

            var bytes = r.Bytes(size);
            if (bytes.Length < size)
            {
                return;
            }

            switch (datum.Type)
            {
                case SimConnectDataType.InitPosition when InitPosition.Read(bytes) is { } p:
                    target.Set("PLANE LATITUDE", p.Latitude, now);
                    target.Set("PLANE LONGITUDE", p.Longitude, now);
                    target.Set("PLANE ALTITUDE", p.AltitudeFeet, now);
                    target.Set("PLANE PITCH DEGREES", p.Pitch, now);
                    target.Set("PLANE BANK DEGREES", p.Bank, now);
                    target.Set("PLANE HEADING DEGREES TRUE", p.Heading, now);
                    target.Set("SIM ON GROUND", p.OnGround ? 1 : 0, now);
                    break;
                case SimConnectDataType.Int32:
                    SetAi(target, datum, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes), now);
                    break;
                case SimConnectDataType.Int64:
                    SetAi(target, datum, System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(bytes), now);
                    break;
                case SimConnectDataType.Float32:
                    SetAi(target, datum, System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(bytes), now);
                    break;
                case SimConnectDataType.Float64:
                    SetAi(target, datum, System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(bytes), now);
                    break;
            }
        }

    }

    private void SetAi(TrafficObject target, Datum datum, double value, DateTimeOffset now)
    {
        var canonical = TrafficObject.CanonicalUnits(datum.Name);
        if (canonical.Length > 0 && Units.IsKnown(datum.Units) && Units.IsKnown(canonical))
        {
            value = Units.Convert(value, datum.Units, canonical, (from, to) => UnitsPassedThrough(datum.Name, from, to));
        }

        target.Set(datum.Name, value, now);

        // MSFS answered GROUND VELOCITY for BATC's frozen AI with about the airspeed BATC wrote (254 kt for 249 kt at
        // FL224, recording 20260930-174605), not the speed its position writes imply, so mirror that.
        if (datum.Name.Equals("AIRSPEED INDICATED", StringComparison.OrdinalIgnoreCase))
        {
            target.Set("GROUND VELOCITY", value, now);
        }

        // On the ground BATC writes height above ground instead of altitude (define 5, define 14); in the air it writes
        // altitude (define 4). SIM ON GROUND follows from whichever it wrote.
        if (datum.Name.Equals("PLANE ALT ABOVE GROUND", StringComparison.OrdinalIgnoreCase))
        {
            target.Set("SIM ON GROUND", value < 50 ? 1 : 0, now); // BATC writes the CG height: 10-15 ft for a taxiing A320 (live test 2026-10-01)
            if (GroundFeet(target) is { } groundFeet)
            {
                target.Set("PLANE ALTITUDE", groundFeet + value, now);
            }
        }
        else if (datum.Name.Equals("PLANE ALTITUDE", StringComparison.OrdinalIgnoreCase) && GroundFeet(target) is { } groundBelow)
        {
            target.Set("SIM ON GROUND", value - groundBelow < 50 ? 1 : 0, now); // parked aircraft sit 10-20 ft up (gear)
        }
    }

    private double? GroundFeet(TrafficObject target)
    {
        if (target.Latitude == 0 && target.Longitude == 0)
        {
            return null;
        }

        if (ground?.ElevationMeters(target.Latitude, target.Longitude) is { } meters)
        {
            return meters / 0.3048;
        }

        // Fall back to the ground under the user's aircraft (same airport, most of the time).
        var (value, status) = resolver.Resolve("GROUND ALTITUDE", "feet");
        return status == ResolveStatus.Ok ? value.Number : null;
    }

    private async Task SendTrafficDataAsync(DataRequest request, TrafficObject ai)
    {
        List<Datum>? datums;
        lock (_gate)
        {
            datums = _definitions.GetValueOrDefault(request.DefineId)?.ToList();
        }

        if (datums is null)
        {
            LogOnce($"nodef:{request.DefineId}", $"#{connectionId} request {request.RequestId} for unknown define {request.DefineId}");
            return;
        }

        var groundFeet = GroundFeet(ai) ?? 0;
        using var data = new MemoryStream();
        foreach (var datum in datums)
        {
            var name = datum.Name.ToUpperInvariant();
            var feet = name switch
            {
                "GROUND ALTITUDE" => groundFeet,
                "PLANE ALT ABOVE GROUND" or "PLANE ALT ABOVE GROUND MINUS CG" => Math.Max(0, ai.AltitudeFeet - groundFeet),
                _ => (double?)null,
            };
            var value = feet ?? ai.Get(datum.Name);
            var canonical = feet is not null ? "feet" : TrafficObject.CanonicalUnits(datum.Name);
            if (canonical.Length > 0 && Units.IsKnown(datum.Units) && Units.IsKnown(canonical))
            {
                value = Units.Convert(value, canonical, datum.Units, (from, to) => UnitsPassedThrough(datum.Name, from, to));
            }

            if (request.Flags.HasFlag(DataRequestFlags.Tagged))
            {
                data.Write(BitConverter.GetBytes(datum.DatumId));
            }

            DataEncoder.Write(data, datum.Type, DataEncoder.IsText(datum.Type) ? SimValue.Of(name == "TITLE" ? ai.Title : ai.TailNumber) : SimValue.Of(value));
        }

        Stats.DataSent();
        await send(PacketBuilder.Server(RecvId.SimObjectData, _protocol)
            .U32(request.RequestId)
            .U32(ai.ObjectId)
            .U32(request.DefineId)
            .U32((uint)request.Flags)
            .U32(1)
            .U32(1)
            .U32((uint)datums.Count)
            .Bytes(data.ToArray())
            .Build()).ConfigureAwait(false);
    }

    private async Task RequestFacilityAsync(string icao, uint defineId, uint requestId, uint sendId)
    {
        FacilityRequest request;
        lock (_gate)
        {
            _facilityEncoder ??= new FacilityEncoder(_protocol);
            var definition = _facilityDefinitions.GetValueOrDefault(defineId);
            if (definition is { UnknownFields.Count: > 0 })
            {
                LogOnce($"facilitydef:{defineId}", $"#{connectionId} facility define {defineId}: unknown fields sent as zeros: {string.Join(", ", definition.UnknownFields)}");
            }

            request = new FacilityRequest(icao, defineId, requestId, definition is { IsComplete: true } ? definition.Root : null, _facilityEncoder);
        }

        if (facilities is not null)
        {
            // Building an airport from X-Plane's files can take a moment the first time; keep it off the caller.
            var started = _time.GetTimestamp();
            (IReadOnlyList<Packet>, string)? answer;
            try
            {
                answer = await Task.Run(() => facilities is FacilitySourceChain chain
                    ? chain.Answer(request)
                    : facilities.Find(request) is { } found ? (found, facilities.Description) : null).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Rule 2: an airport we cannot build (a damaged apt.dat or CIFP file) gets the "no such airport"
                // exception below, so BATC carries on instead of waiting for an answer that never comes.
                log($"#{connectionId} RequestFacilityData {icao} define {defineId}: failed: {ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}");
                answer = null;
            }
            if (answer is { } reply)
            {
                var ms = _time.GetElapsedTime(started).TotalMilliseconds;
                log($"#{connectionId} RequestFacilityData {icao} define {defineId}: {reply.Item1.Count} packets from {reply.Item2} in {ms:0} ms");
                Stats.Airport(new AirportServed(icao, reply.Item2, reply.Item1.Count, ms, _time.GetUtcNow()));
                foreach (var packet in reply.Item1)
                {
                    await send(packet).ConfigureAwait(false);
                }

                await send(PacketBuilder.Server(RecvId.FacilityDataEnd, _protocol).U32(requestId).Build()).ConfigureAwait(false);
                return;
            }
        }

        // MSFS answers an airport it does not have with exception 1, index 3 (recorded for YSAM); BATC carries on.
        log($"#{connectionId} RequestFacilityData {icao} define {defineId}: no data, sent exception");
        Stats.Airport(new AirportServed(icao, null, 0, 0, _time.GetUtcNow()));
        await SendExceptionAsync(SimConnectException.Error, sendId, 3).ConfigureAwait(false);
    }

    private Task SendExceptionAsync(SimConnectException exception, uint sendId, uint index = 0) =>
        send(PacketBuilder.Server(RecvId.Exception, _protocol).U32((uint)exception).U32(sendId).U32(index).Build());

    private void UnitsPassedThrough(string name, string from, string to) =>
        LogOnce($"units:{from}>{to}", $"#{connectionId} UNITS cannot convert '{from}' to '{to}' (first seen for {name}); value passed on unchanged");

    private void LogOnce(string key, string message)
    {
        lock (_gate)
        {
            if (_loggedOnce.Contains(key))
            {
                return;
            }

            // Keys include client-chosen ids (object ids, define ids, names), so bound the set for long sessions.
            if (_loggedOnce.Count >= MaxLoggedOnce)
            {
                if (_suppressedLogged)
                {
                    return;
                }

                _suppressedLogged = true;
                message = $"#{connectionId} {MaxLoggedOnce} one-time messages logged; further one-time messages suppressed for this connection";
            }
            else
            {
                _loggedOnce.Add(key);
            }
        }

        log(message);
    }

    private static bool IsPosition(string simVar) =>
        simVar.Equals("PLANE LATITUDE", StringComparison.OrdinalIgnoreCase) || simVar.Equals("PLANE LONGITUDE", StringComparison.OrdinalIgnoreCase);

    /// <summary>The sim clock and date. Sent as zero they read as midnight on 1 January of year 1.</summary>
    private static bool IsClock(string simVar) =>
        simVar.ToUpperInvariant() is "ZULU TIME" or "LOCAL TIME" or "ZULU DAY OF YEAR" or "ZULU DAY OF WEEK" or "ZULU YEAR"
            or "ZULU MONTH OF YEAR" or "ZULU DAY OF MONTH" or "LOCAL DAY OF YEAR" or "LOCAL YEAR" or "ABSOLUTE TIME";

    private static uint U(Dictionary<string, object?> f, string key) => f.GetValueOrDefault(key) is uint u ? u : 0;

    private static string S(Dictionary<string, object?> f, string key) => f.GetValueOrDefault(key) as string ?? "";

    private sealed record Datum(string Name, string Units, SimConnectDataType Type, float Epsilon, uint DatumId);

    private sealed record DataRequest(
        uint RequestId,
        uint DefineId,
        uint ObjectId,
        SimConnectPeriod Period,
        DataRequestFlags Flags,
        uint Interval,
        uint Limit)
    {
        public DateTimeOffset NextDue { get; set; }

        public byte[]? LastData { get; set; }

        public uint Sent { get; set; }
    }
}

/// <summary>One airport request answered, or not (<see cref="Source"/> null), for the status page.</summary>
public sealed record AirportServed(string Icao, string? Source, int Packets, double Milliseconds, DateTimeOffset At);

/// <summary>Counters for the status page and logs. Thread-safe.</summary>
public sealed class SessionStats(DateTimeOffset connectedAt)
{
    /// <summary>Most recent airport requests kept for the status page.</summary>
    public const int AirportHistory = 20;

    private readonly ConcurrentQueue<AirportServed> _airports = new();
    private long _received;
    private long _lastPacketTicks = connectedAt.UtcTicks;
    private readonly ConcurrentDictionary<string, long> _calls = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _events = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _unmapped = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _unhandled = new(StringComparer.Ordinal);
    private long _dataSent;

    public IReadOnlyDictionary<string, long> Calls => _calls;

    public IReadOnlyDictionary<string, long> Events => _events;

    public IReadOnlyCollection<string> UnmappedNames => _unmapped.Keys.ToList();

    public IReadOnlyCollection<string> PendingNames => _pending.Keys.ToList();

    public IReadOnlyCollection<string> UnhandledCalls => _unhandled.Keys.ToList();

    public long DataPacketsSent => Interlocked.Read(ref _dataSent);

    public DateTimeOffset ConnectedAt => connectedAt;

    public long PacketsReceived => Interlocked.Read(ref _received);

    public DateTimeOffset LastPacketAt => new(Interlocked.Read(ref _lastPacketTicks), TimeSpan.Zero);

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<AirportServed> Airports => [.. _airports];

    internal void Count(string call, DateTimeOffset at)
    {
        _calls.AddOrUpdate(call, 1, (_, n) => n + 1);
        Interlocked.Increment(ref _received);
        Interlocked.Exchange(ref _lastPacketTicks, at.UtcTicks);
    }

    internal void Airport(AirportServed served)
    {
        _airports.Enqueue(served);
        while (_airports.Count > AirportHistory && _airports.TryDequeue(out _))
        {
        }
    }

    internal void Event(string name) => _events.AddOrUpdate(name, 1, (_, n) => n + 1);

    internal void Unmapped(string name) => _unmapped.TryAdd(name, 0);

    internal void Pending(string name) => _pending.TryAdd(name, 0);

    internal void Unhandled(string name) => _unhandled.TryAdd(name, 0);

    internal void DataSent() => Interlocked.Increment(ref _dataSent);
}

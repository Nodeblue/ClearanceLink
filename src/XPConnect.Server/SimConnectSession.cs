using System.Collections.Concurrent;
using XPConnect.Protocol;
using XPConnect.Protocol.Facilities;

namespace XPConnect.Server;

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
    TimeProvider? time = null)
{
    /// <summary>Object id MSFS 2024 reports for the user aircraft in SimObjectData (recorded 2026-09-30).</summary>
    public const uint UserObjectId = 524288;

    private static readonly TimeSpan FramePeriod = TimeSpan.FromMilliseconds(1000.0 / 30);
    private static readonly TimeSpan SecondPeriod = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<uint, List<Datum>> _definitions = [];
    private readonly Dictionary<uint, DataRequest> _requests = [];
    private readonly Dictionary<uint, string> _clientEvents = [];
    private readonly Dictionary<uint, FacilityDefinition> _facilityDefinitions = [];
    private FacilityEncoder? _facilityEncoder;
    private readonly HashSet<string> _loggedOnce = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private uint _protocol = 6;

    public int ConnectionId => connectionId;

    public string ClientName { get; private set; } = "";

    public SessionStats Stats { get; } = new();

    public async Task HandleAsync(Packet packet)
    {
        if (packet.Direction != Direction.ClientToServer)
        {
            return;
        }

        Stats.Count(packet.Name);
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

            // Not answered, as in the stand-in test of 2026-09-30 where BATC ran fine without them: BATC waits for the
            // livery list before creating AI traffic, so leaving it unanswered keeps traffic off (AI needs the plugin).
            case 0x5B: // EnumerateSimObjectsAndLiveries
            case 0x27 or 0x28 or 0x29 or 0x2A or 0x57 or 0x58 or 0x59 or 0x5A: // AI object creation
                LogOnce($"ai:{packet.Name}", $"#{connectionId} {packet.Name} not answered: AI traffic is not supported yet");
                break;

            case 0x10: // SetDataOnSimObject (AI traffic positions, L-vars): accepted, not applied yet
                LogOnce($"setdata:{f["defineId"]}", $"#{connectionId} SetDataOnSimObject define {f["defineId"]} ignored");
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
            LogOnce($"object:{request.ObjectId}", $"#{connectionId} data requested for object {request.ObjectId}; only the user aircraft exists, not answered");
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
        // delivered a position. BATC keeps asking (it polls with ONCE), and periodic requests retry on the next tick.
        if (!xplane.IsConnected || datums.Any(d => IsPosition(d.Name) && resolver.Resolve(d.Name, d.Units).Status != ResolveStatus.Ok))
        {
            LogOnce("waiting-xplane", $"#{connectionId} holding aircraft data until X-Plane is connected and reports a position");
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
            var (value, status) = resolver.Resolve(datum.Name, datum.Units);
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
        // BATC also sends events (lights, throttle, gear) to its AI aircraft; only the user aircraft maps to X-Plane.
        if (objectId is not (0 or UserObjectId))
        {
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
                var value = Units.Convert(unchecked((int)data), map.FromUnits, map.ToUnits);
                await xplane.WriteAsync(dataref, value).ConfigureAwait(false);
                log($"#{connectionId} {name}({data}) -> {dataref} = {value}");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or HttpRequestException)
        {
            log($"#{connectionId} {name} failed: {ex.Message}");
        }
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
            var answer = await Task.Run(() => facilities is FacilitySourceChain chain
                ? chain.Answer(request)
                : facilities.Find(request) is { } found ? (found, facilities.Description) : null).ConfigureAwait(false);
            if (answer is { } reply)
            {
                log($"#{connectionId} RequestFacilityData {icao} define {defineId}: {reply.Item1.Count} packets from {reply.Item2} in {_time.GetElapsedTime(started).TotalMilliseconds:0} ms");
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
        await SendExceptionAsync(SimConnectException.Error, sendId, 3).ConfigureAwait(false);
    }

    private Task SendExceptionAsync(SimConnectException exception, uint sendId, uint index = 0) =>
        send(PacketBuilder.Server(RecvId.Exception, _protocol).U32((uint)exception).U32(sendId).U32(index).Build());

    private void LogOnce(string key, string message)
    {
        lock (_gate)
        {
            if (!_loggedOnce.Add(key))
            {
                return;
            }
        }

        log(message);
    }

    private static bool IsPosition(string simVar) =>
        simVar.Equals("PLANE LATITUDE", StringComparison.OrdinalIgnoreCase) || simVar.Equals("PLANE LONGITUDE", StringComparison.OrdinalIgnoreCase);

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

/// <summary>Counters for the dashboard and logs. Thread-safe.</summary>
public sealed class SessionStats
{
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

    internal void Count(string call) => _calls.AddOrUpdate(call, 1, (_, n) => n + 1);

    internal void Event(string name) => _events.AddOrUpdate(name, 1, (_, n) => n + 1);

    internal void Unmapped(string name) => _unmapped.TryAdd(name, 0);

    internal void Pending(string name) => _pending.TryAdd(name, 0);

    internal void Unhandled(string name) => _unhandled.TryAdd(name, 0);

    internal void DataSent() => Interlocked.Increment(ref _dataSent);
}

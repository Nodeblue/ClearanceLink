using XPConnect.Host.Transport;
using XPConnect.Protocol;
using XPConnect.Recorder;

namespace XPConnect.Host;

/// <summary>
/// Pass-through recorder: sits between a SimConnect client (BATC) and the real simulator, forwards every packet
/// unchanged and records both directions.
/// </summary>
public sealed class RecordingProxy(IListener listener, Endpoint upstream, JsonlRecorder recorder)
{
    private int _connections;

    public async Task RunAsync(CancellationToken ct)
    {
        PacketConsole.Info($"Recording proxy listening on {listener.Endpoint}, forwarding to {upstream}");
        while (!ct.IsCancellationRequested)
        {
            var client = await listener.AcceptAsync(ct).ConfigureAwait(false);
            var id = Interlocked.Increment(ref _connections);
            _ = Task.Run(() => HandleAsync(client, id, ct), ct);
        }
    }

    private async Task HandleAsync(Stream client, int id, CancellationToken ct)
    {
        PacketConsole.Info($"Client #{id} connected; connecting upstream to {upstream}");
        try
        {
            await using var server = await Transports.ConnectAsync(upstream, ct).ConfigureAwait(false);
            await using (client.ConfigureAwait(false))
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var up = PumpAsync(client, server, Direction.ClientToServer, id, linked.Token);
                var down = PumpAsync(server, client, Direction.ServerToClient, id, linked.Token);
                await Task.WhenAny(up, down).ConfigureAwait(false);
                await linked.CancelAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            PacketConsole.Info($"Client #{id} error: {ex.Message}");
        }

        PacketConsole.Info($"Client #{id} disconnected");
    }

    private async Task PumpAsync(Stream from, Stream to, Direction direction, int id, CancellationToken ct)
    {
        try
        {
            while (await PacketFramer.ReadAsync(from, direction, ct).ConfigureAwait(false) is { } packet)
            {
                PacketConsole.Print(recorder.Record(packet, id));
                await PacketFramer.WriteAsync(to, packet, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // Either side closing ends the session.
        }
    }
}

/// <summary>
/// Stand-in simulator: answers Open with the chosen identity and serves the user aircraft's SimVars from
/// <see cref="FakeSimState"/>. Every request is recorded; calls it cannot answer are logged and left unanswered.
/// </summary>
public sealed class FakeSimServer(IListener listener, ServerIdentity identity, JsonlRecorder recorder, FakeSimState? state = null, FacilityReplay? facilities = null)
{
    /// <summary>Object id MSFS 2024 reports for the user aircraft in SimObjectData (recorded 2026-09-30).</summary>
    public const uint UserObjectId = 524288;

    private readonly FakeSimState _state = state ?? new FakeSimState();
    private int _connections;

    public async Task RunAsync(CancellationToken ct)
    {
        PacketConsole.Info($"Fake simulator ({identity.ApplicationName}) listening on {listener.Endpoint}");
        while (!ct.IsCancellationRequested)
        {
            var client = await listener.AcceptAsync(ct).ConfigureAwait(false);
            var id = Interlocked.Increment(ref _connections);
            _ = Task.Run(
                async () =>
                {
                    using var session = new Session(this, client, id);
                    await session.RunAsync(ct).ConfigureAwait(false);
                },
                ct);
        }
    }

    private sealed class Session(FakeSimServer server, Stream client, int id) : IDisposable
    {
        private readonly Dictionary<uint, DataDefinition> _definitions = [];
        private readonly Dictionary<uint, string> _events = [];
        private readonly Dictionary<uint, CancellationTokenSource> _periodic = [];
        private readonly HashSet<string> _unknown = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public void Dispose() => _writeLock.Dispose();

        public async Task RunAsync(CancellationToken ct)
        {
            PacketConsole.Info($"Client #{id} connected");
            using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await using (client.ConfigureAwait(false))
            {
                try
                {
                    while (await PacketFramer.ReadAsync(client, Direction.ClientToServer, session.Token).ConfigureAwait(false) is { } packet)
                    {
                        PacketConsole.Print(server.Record(packet, id));
                        await HandleAsync(packet, session.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidDataException)
                {
                    PacketConsole.Info($"Client #{id}: {ex.Message}");
                }

                await session.CancelAsync().ConfigureAwait(false);
            }

            PacketConsole.Info($"Client #{id} disconnected");
        }

        private async Task HandleAsync(Packet packet, CancellationToken ct)
        {
            var r = new PayloadReader(packet.Payload);
            switch (packet.FunctionId)
            {
                case 0x01: // Open
                    await SendAsync(server.Identity.BuildOpenReply(packet.Protocol), ct).ConfigureAwait(false);
                    break;
                case 0x04: // MapClientEventToSimEvent
                    var eventId = r.U32();
                    _events[eventId] = r.Str(PacketDecoder.NameLength);
                    break;
                case 0x05: // TransmitClientEvent
                    var objectId = r.U32();
                    var name = _events.GetValueOrDefault(r.U32());
                    var data = r.U32();
                    if (objectId == 0 && name is not null && server._state.ApplyEvent(name, data) is { } change)
                    {
                        PacketConsole.Info($"Client #{id} {name}: {change}");
                    }

                    break;
                case 0x0C: // AddToDataDefinition
                    var defineId = r.U32();
                    if (!_definitions.TryGetValue(defineId, out var definition))
                    {
                        _definitions[defineId] = definition = new DataDefinition();
                    }

                    definition.Add(new Datum(r.Str(PacketDecoder.NameLength), r.Str(PacketDecoder.NameLength), r.U32()));
                    break;
                case 0x0D: // ClearDataDefinition
                    _definitions.Remove(r.U32());
                    break;
                case 0x0E: // RequestDataOnSimObject
                    await RequestDataAsync(packet.Protocol, r.U32(), r.U32(), r.U32(), r.U32(), ct).ConfigureAwait(false);
                    break;
                case 0x46 when server.Facilities is { } facilities: // RequestFacilityData
                    var facilityDefineId = r.U32();
                    var facilityRequestId = r.U32();
                    var icao = r.Str(16);
                    if (facilities.Find(icao, facilityDefineId, facilityRequestId) is { } replies)
                    {
                        PacketConsole.Info($"Client #{id} RequestFacilityData {icao} define {facilityDefineId}: replaying {replies.Count} packets");
                        foreach (var reply in replies)
                        {
                            await SendAsync(reply, ct).ConfigureAwait(false);
                        }

                        await SendAsync(
                            PacketBuilder.Server(RecvId.FacilityDataEnd, packet.Protocol).U32(facilityRequestId).Build(),
                            ct).ConfigureAwait(false);
                    }
                    else
                    {
                        // MSFS answers an airport it does not have with exception 1, index 3 (recorded for YSAM).
                        PacketConsole.Info($"Client #{id} RequestFacilityData {icao} define {facilityDefineId}: not in the recording, sending exception");
                        await SendAsync(
                            PacketBuilder.Server(RecvId.Exception, packet.Protocol).U32(1).U32(packet.SendId ?? 0).U32(3).Build(),
                            ct).ConfigureAwait(false);
                    }

                    break;
            }
        }

        private async Task RequestDataAsync(uint protocol, uint requestId, uint defineId, uint objectId, uint period, CancellationToken ct)
        {
            if (_periodic.Remove(requestId, out var previous))
            {
                await previous.CancelAsync().ConfigureAwait(false);
                previous.Dispose();
            }

            // Only the user aircraft exists here. period: 0 never, 1 once, 2 visual frame, 3 sim frame, 4 second.
            if (period == 0 || (objectId != 0 && objectId != UserObjectId) || !_definitions.TryGetValue(defineId, out var definition))
            {
                return;
            }

            if (period == 1)
            {
                await SendAsync(BuildData(protocol, requestId, defineId, definition), ct).ConfigureAwait(false);
                return;
            }

            var interval = period == 4 ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(50);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _periodic[requestId] = cts;
            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        using var timer = new PeriodicTimer(interval);
                        do
                        {
                            await SendAsync(BuildData(protocol, requestId, defineId, definition), cts.Token).ConfigureAwait(false);
                        }
                        while (await timer.WaitForNextTickAsync(cts.Token).ConfigureAwait(false));
                    }
                    catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                    {
                        // The session ended or the request was replaced.
                    }
                },
                CancellationToken.None);
        }

        private Packet BuildData(uint protocol, uint requestId, uint defineId, DataDefinition definition)
        {
            var block = definition.Pack(datum =>
            {
                var value = server._state.Get(datum.Name);
                if (value is null && _unknown.Add(datum.Name))
                {
                    PacketConsole.Info($"Client #{id} no value for SimVar '{datum.Name}' (define {defineId}); sending zero");
                }

                return value;
            });
            return PacketBuilder.Server(RecvId.SimObjectData, protocol)
                .U32(requestId).U32(UserObjectId).U32(defineId).U32(0).U32(1).U32(1).U32((uint)definition.Datums.Count)
                .Bytes(block)
                .Build();
        }

        private async Task SendAsync(Packet packet, CancellationToken ct)
        {
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                PacketConsole.Print(server.Record(packet, id));
                await PacketFramer.WriteAsync(client, packet, ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }

    private ServerIdentity Identity => identity;

    private FacilityReplay? Facilities => facilities;

    private RecordEntry? Record(Packet packet, int connection) => recorder.Record(packet, connection);
}

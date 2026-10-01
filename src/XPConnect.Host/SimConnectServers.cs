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
/// Minimal stand-in simulator for the Phase 0 spike: answers Open with the chosen identity and records everything
/// else the client asks for without answering it.
/// </summary>
public sealed class FakeSimServer(IListener listener, ServerIdentity identity, JsonlRecorder recorder)
{
    private int _connections;

    public async Task RunAsync(CancellationToken ct)
    {
        PacketConsole.Info($"Fake simulator ({identity.ApplicationName}) listening on {listener.Endpoint}");
        while (!ct.IsCancellationRequested)
        {
            var client = await listener.AcceptAsync(ct).ConfigureAwait(false);
            var id = Interlocked.Increment(ref _connections);
            _ = Task.Run(() => HandleAsync(client, id, ct), ct);
        }
    }

    private async Task HandleAsync(Stream client, int id, CancellationToken ct)
    {
        PacketConsole.Info($"Client #{id} connected");
        await using (client.ConfigureAwait(false))
        {
            try
            {
                while (await PacketFramer.ReadAsync(client, Direction.ClientToServer, ct).ConfigureAwait(false) is { } packet)
                {
                    PacketConsole.Print(recorder.Record(packet, id));
                    if (packet.FunctionId == 0x01)
                    {
                        var reply = identity.BuildOpenReply(packet.Protocol);
                        PacketConsole.Print(recorder.Record(reply, id));
                        await PacketFramer.WriteAsync(client, reply, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidDataException)
            {
                PacketConsole.Info($"Client #{id}: {ex.Message}");
            }
        }

        PacketConsole.Info($"Client #{id} disconnected");
    }
}

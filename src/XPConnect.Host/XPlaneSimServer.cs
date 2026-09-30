using XPConnect.Host.Transport;
using XPConnect.Protocol;
using XPConnect.Recorder;
using XPConnect.Server;

namespace XPConnect.Host;

/// <summary>
/// The real stand-in: one <see cref="SimConnectSession"/> per client, answering from X-Plane through the mapping
/// tables. Every packet both ways is recorded, like the other modes.
/// </summary>
public sealed class XPlaneSimServer(
    IListener listener,
    ServerIdentity identity,
    JsonlRecorder recorder,
    SimVarResolver resolver,
    IXPlaneData xplane,
    IFacilitySource? facilities = null)
{
    /// <summary>How often periodic data (SECOND, frame periods) is checked; about one sim frame.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(33);

    private readonly List<SimConnectSession> _sessions = [];
    private int _connections;

    public IReadOnlyList<SimConnectSession> Sessions
    {
        get
        {
            lock (_sessions)
            {
                return [.. _sessions];
            }
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        PacketConsole.Info($"XPConnect ({identity.ApplicationName}) listening on {listener.Endpoint}, answering from X-Plane");
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
        using var writeLock = new SemaphoreSlim(1, 1);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);

        async Task SendAsync(Packet packet)
        {
            await writeLock.WaitAsync(stop.Token).ConfigureAwait(false);
            try
            {
                PacketConsole.Print(recorder.Record(packet, id));
                await PacketFramer.WriteAsync(client, packet, stop.Token).ConfigureAwait(false);
            }
            finally
            {
                writeLock.Release();
            }
        }

        var session = new SimConnectSession(id, identity, resolver, xplane, SendAsync, PacketConsole.Info, facilities);
        lock (_sessions)
        {
            _sessions.Add(session);
        }

        await using (client.ConfigureAwait(false))
        {
            var ticker = TickAsync(session, id, stop.Token);
            try
            {
                while (await PacketFramer.ReadAsync(client, Direction.ClientToServer, stop.Token).ConfigureAwait(false) is { } packet)
                {
                    PacketConsole.Print(recorder.Record(packet, id));
                    await session.HandleAsync(packet).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidDataException)
            {
                PacketConsole.Info($"Client #{id}: {ex.Message}");
            }

            await stop.CancelAsync().ConfigureAwait(false);
            await ticker.ConfigureAwait(false);
        }

        lock (_sessions)
        {
            _sessions.Remove(session);
        }

        var stats = session.Stats;
        PacketConsole.Info(
            $"Client #{id} '{session.ClientName}' disconnected: {stats.DataPacketsSent} data packets sent; " +
            $"unmapped: [{string.Join(", ", stats.UnmappedNames)}]; waiting on X-Plane: [{string.Join(", ", stats.PendingNames)}]");
    }

    private static async Task TickAsync(SimConnectSession session, int id, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await session.TickAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // The session ended.
        }
        catch (Exception ex) when (ex is InvalidOperationException)
        {
            PacketConsole.Info($"Client #{id} periodic data stopped: {ex.Message}");
        }
    }
}

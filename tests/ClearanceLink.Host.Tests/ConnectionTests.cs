using System.Net.Sockets;
using System.Threading.Channels;
using ClearanceLink.Host.Support;
using ClearanceLink.Host.Transport;

namespace ClearanceLink.Host.Tests;

public sealed class ConnectionTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public void Gate_reports_changes_once_and_swaps_the_while_closed_token()
    {
        var running = false;
        var log = new List<string>();
        using var gate = new MsfsGate(() => running, log.Add);
        var changes = new List<bool>();
        gate.Changed += changes.Add;

        gate.Check();
        var token = gate.WhileClosed;
        gate.Check();
        running = true;
        gate.Check();

        Assert.True(gate.IsRunning);
        Assert.True(token.IsCancellationRequested);
        running = false;
        gate.Check();

        Assert.False(gate.WhileClosed.IsCancellationRequested);
        Assert.Equal([false, true, false], changes);
        Assert.Equal(3, log.Count);
        Assert.Contains("is running", log[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pipe_waits_while_MSFS_runs_and_drops_its_clients_when_MSFS_starts()
    {
        var running = true;
        using var gate = new MsfsGate(() => running, _ => { });
        var inner = new FakeListener();
        await using var pipe = new GatedPipeListener(inner, gate);
        using var cts = new CancellationTokenSource(Wait);

        var accept = pipe.AcceptAsync(cts.Token);
        await Task.Delay(100, cts.Token);
        Assert.Equal(0, inner.Waiting);
        Assert.False(accept.IsCompleted);

        running = false;
        gate.Check();
        var client = new MemoryStream();
        await inner.Offer(client, cts.Token);
        Assert.Same(client, await accept.WaitAsync(cts.Token));

        // A second accept is waiting on the pipe when MSFS starts: it lets go, and the connected client is closed.
        var next = pipe.AcceptAsync(cts.Token);
        await inner.WaitForWaiter(cts.Token);
        running = true;
        gate.Check();
        Assert.False(client.CanRead);
        await inner.WaitForNoWaiter(cts.Token);
        Assert.False(next.IsCompleted);

        running = false;
        gate.Check();
        var later = new MemoryStream();
        await inner.Offer(later, cts.Token);
        Assert.Same(later, await next.WaitAsync(cts.Token));
    }

    [Fact]
    public async Task Multi_listener_keeps_accepting_after_a_transient_accept_error()
    {
        using var cts = new CancellationTokenSource(Wait);
        var inner = new FakeListener { FailNext = 2 };
        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
        await using var multi = new MultiListener([inner], log.Enqueue, TimeSpan.FromMilliseconds(20));

        var accept = multi.AcceptAsync(cts.Token);
        var client = new MemoryStream();
        await inner.Offer(client, cts.Token);

        Assert.Same(client, await accept.WaitAsync(cts.Token));
        Assert.Single(log, l => l.StartsWith($"listen: accept failed on {inner.Endpoint}: IOException: All pipe instances are busy.", StringComparison.Ordinal));
        Assert.Contains($"listen: accepting again on {inner.Endpoint} after 2 failed accept(s)", log);
    }

    [Fact]
    public async Task Multi_listener_accepts_from_every_endpoint()
    {
        using var cts = new CancellationTokenSource(Wait);
        var a = Transports.Listen(Endpoint.Parse("tcp:0"));
        var b = Transports.Listen(Endpoint.Parse("tcp:0"));
        await using var multi = new MultiListener([a, b], _ => { });
        Assert.Equal($"{a.Endpoint} + {b.Endpoint}", multi.Description);

        using var first = new TcpClient();
        using var second = new TcpClient();
        var accepting = multi.AcceptAsync(cts.Token);
        await first.ConnectAsync("127.0.0.1", a.Endpoint.Port, cts.Token);
        await using var one = await accepting;
        await second.ConnectAsync("127.0.0.1", b.Endpoint.Port, cts.Token);
        await using var two = await multi.AcceptAsync(cts.Token);

        Assert.NotNull(one);
        Assert.NotNull(two);
    }

    [Fact]
    public void Connection_setting_is_saved_as_plain_words()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory("clearancelink-settings").FullName, "settings.json");
        Assert.Equal(ConnectionMode.Automatic, AppSettings.Load(file).Connection);

        new AppSettings { Connection = ConnectionMode.AlwaysOn }.Save(file);

        Assert.Contains("\"always-on\"", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Equal(ConnectionMode.AlwaysOn, AppSettings.Load(file).Connection);
    }

    /// <summary>A listener the test hands clients to, counting accepts in progress.</summary>
    private sealed class FakeListener : IListener
    {
        private readonly Channel<Stream> _clients = Channel.CreateUnbounded<Stream>();
        private int _waiting;

        public Endpoint Endpoint { get; } = Endpoint.Parse("pipe");

        public int Waiting => Volatile.Read(ref _waiting);

        public int FailNext { get; set; }

        public async Task<Stream> AcceptAsync(CancellationToken ct)
        {
            if (FailNext > 0)
            {
                FailNext--;
                throw new IOException("All pipe instances are busy.");
            }

            Interlocked.Increment(ref _waiting);
            try
            {
                return await _clients.Reader.ReadAsync(ct);
            }
            finally
            {
                Interlocked.Decrement(ref _waiting);
            }
        }

        public async Task Offer(Stream client, CancellationToken ct)
        {
            await WaitForWaiter(ct);
            await _clients.Writer.WriteAsync(client, ct);
        }

        public async Task WaitForWaiter(CancellationToken ct)
        {
            while (Waiting == 0)
            {
                await Task.Delay(10, ct);
            }
        }

        public async Task WaitForNoWaiter(CancellationToken ct)
        {
            while (Waiting != 0)
            {
                await Task.Delay(10, ct);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

using XPConnect.Host;
using XPConnect.Host.Transport;
using XPConnect.Protocol;
using XPConnect.Protocol.Tests;
using XPConnect.Recorder;

namespace XPConnect.Host.Tests;

public sealed class ServerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("xpconnect-tests").FullName;

    public ServerTests() => PacketConsole.Verbose = false;

    [Theory]
    [InlineData("tcp:0")]
    [InlineData("pipe")]
    public async Task Fake_server_answers_open_and_records_requests(string listenAt)
    {
        var endpoint = Endpoint.Parse(listenAt);
        if (endpoint.Kind == EndpointKind.Pipe)
        {
            endpoint = endpoint with { PipeName = $"xpconnect-test-{Guid.NewGuid():N}" };
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var path = Path.Combine(_dir, "fake.jsonl");
        using (var recorder = new JsonlRecorder(path))
        {
            var recorded = 0;
            recorder.Recorded += _ => Interlocked.Increment(ref recorded);
            await using var listener = Transports.Listen(endpoint);
            var server = new FakeSimServer(listener, ServerIdentity.Msfs2024, recorder).RunAsync(cts.Token);

            await using var client = await Transports.ConnectAsync(listener.Endpoint, cts.Token);
            await PacketFramer.WriteAsync(client, TestPackets.Open(), cts.Token);
            var reply = await PacketFramer.ReadAsync(client, Direction.ServerToClient, cts.Token);
            await PacketFramer.WriteAsync(client, TestPackets.AddToDataDefinition(1, "PLANE ALTITUDE", "feet"), cts.Token);
            await WaitForAsync(() => Volatile.Read(ref recorded) >= 3, cts.Token);

            Assert.Equal(RecvId.Open, (RecvId)reply!.TypeField);
            Assert.Equal("SunRise", PacketDecoder.Decode(reply)["applicationName"]);
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => server);
        }

        var entries = JsonlRecorder.ReadAll(path).ToList();
        Assert.Equal(["Open", "Open", "AddToDataDefinition"], entries.Select(e => e.Name));
        Assert.Equal(["c2s", "s2c", "c2s"], entries.Select(e => e.Dir));
        Assert.Equal("PLANE ALTITUDE", entries[2].Field("datumName"));
    }

    [Fact]
    public async Task Recording_proxy_forwards_both_ways_and_builds_inventory()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var upstreamPath = Path.Combine(_dir, "upstream.jsonl");
        var proxyPath = Path.Combine(_dir, "proxy.jsonl");
        using var upstreamRecorder = new JsonlRecorder(upstreamPath);
        using var proxyRecorder = new JsonlRecorder(proxyPath);
        var inventory = new CallInventory();
        proxyRecorder.Recorded += inventory.Add;

        await using var upstreamListener = Transports.Listen(Endpoint.Parse("tcp:0"));
        _ = new FakeSimServer(upstreamListener, ServerIdentity.Msfs2020, upstreamRecorder).RunAsync(cts.Token);
        await using var proxyListener = Transports.Listen(Endpoint.Parse("tcp:0"));
        _ = new RecordingProxy(proxyListener, upstreamListener.Endpoint, proxyRecorder).RunAsync(cts.Token);

        await using var client = await Transports.ConnectAsync(proxyListener.Endpoint, cts.Token);
        await PacketFramer.WriteAsync(client, TestPackets.Open(), cts.Token);
        var reply = await PacketFramer.ReadAsync(client, Direction.ServerToClient, cts.Token);
        await PacketFramer.WriteAsync(client, TestPackets.AddToDataDefinition(1, "COM ACTIVE FREQUENCY:1", "MHz"), cts.Token);
        await PacketFramer.WriteAsync(client, TestPackets.SubscribeToSystemEvent(1, "Pause"), cts.Token);
        await WaitForAsync(() => inventory.Count >= 4, cts.Token);

        Assert.Equal("KittyHawk", PacketDecoder.Decode(reply!)["applicationName"]);
        var markdown = inventory.ToMarkdown("test");
        Assert.Contains("COM ACTIVE FREQUENCY:1 | MHz | type 4", markdown);
        Assert.Contains("`Pause`", markdown);
        Assert.Contains("server: KittyHawk", markdown);
        Assert.Contains("client: 'BeyondATC'", markdown);
    }

    [Theory]
    [InlineData("pipe", EndpointKind.Pipe, @"Microsoft Flight Simulator\SimConnect", 0)]
    [InlineData(@"pipe:\\.\pipe\Foo", EndpointKind.Pipe, "Foo", 0)]
    [InlineData("tcp:500", EndpointKind.Tcp, "", 500)]
    [InlineData("tcp:10.0.0.2:500", EndpointKind.Tcp, "", 500)]
    public void Parses_endpoints(string text, EndpointKind kind, string pipe, int port)
    {
        var e = Endpoint.Parse(text);

        Assert.Equal(kind, e.Kind);
        Assert.Equal(pipe, e.PipeName);
        Assert.Equal(port, e.Port);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            await Task.Delay(20, ct);
        }
    }
}

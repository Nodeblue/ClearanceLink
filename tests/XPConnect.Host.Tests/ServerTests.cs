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

    [Fact]
    public async Task Fake_server_serves_user_aircraft_data_and_applies_com_events()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var recorder = new JsonlRecorder(Path.Combine(_dir, "data.jsonl"));
        await using var listener = Transports.Listen(Endpoint.Parse("tcp:0"));
        _ = new FakeSimServer(listener, ServerIdentity.Msfs2024, recorder).RunAsync(cts.Token);

        await using var client = await Transports.ConnectAsync(listener.Endpoint, cts.Token);
        await PacketFramer.WriteAsync(client, TestPackets.Open(), cts.Token);
        await PacketFramer.ReadAsync(client, Direction.ServerToClient, cts.Token);
        await PacketFramer.WriteAsync(client, TestPackets.AddToDataDefinition(17, "COM ACTIVE FREQUENCY:1", "Mhz"), cts.Token);
        await PacketFramer.WriteAsync(client, TestPackets.AddToDataDefinition(17, "SIM ON GROUND", "", dataType: 1), cts.Token);
        await PacketFramer.WriteAsync(client, TestPackets.AddToDataDefinition(17, "ATC MODEL", "", dataType: 9), cts.Token);

        // Once (period 1): one reply per request.
        await PacketFramer.WriteAsync(client, TestPackets.RequestDataOnSimObject(1004, 17, period: 1), cts.Token);
        var once = await PacketFramer.ReadAsync(client, Direction.ServerToClient, cts.Token);
        var fields = PacketDecoder.Decode(once!);
        var data = Convert.FromHexString((string)fields["dataHex"]!);

        Assert.Equal(RecvId.SimObjectData, (RecvId)once!.TypeField);
        Assert.Equal(1004u, fields["requestId"]);
        Assert.Equal(FakeSimServer.UserObjectId, fields["objectId"]);
        Assert.Equal(17u, fields["defineId"]);
        Assert.Equal(3u, fields["defineCount"]);
        Assert.Equal(8 + 4 + 256, data.Length);
        Assert.Equal(127.85, BitConverter.ToDouble(data, 0));
        Assert.Equal(1, BitConverter.ToInt32(data, 8));
        Assert.Equal("SF50", System.Text.Encoding.Latin1.GetString(data, 12, 4));

        // COM_RADIO_SET_HZ on the user aircraft changes the active frequency.
        await PacketFramer.WriteAsync(client, TestPackets.MapClientEventToSimEvent(24, "COM_RADIO_SET_HZ"), cts.Token);
        await PacketFramer.WriteAsync(client, TestPackets.TransmitClientEvent(0, 24, 118_000_000), cts.Token);
        await PacketFramer.WriteAsync(client, TestPackets.RequestDataOnSimObject(1004, 17, period: 1), cts.Token);
        var tuned = await PacketFramer.ReadAsync(client, Direction.ServerToClient, cts.Token);
        Assert.Equal(118.0, BitConverter.ToDouble(Convert.FromHexString((string)PacketDecoder.Decode(tuned!)["dataHex"]!), 0));

        // Every second (period 4): replies keep coming without further requests; period 0 stops them.
        await PacketFramer.WriteAsync(client, TestPackets.RequestDataOnSimObject(333, 17, period: 4), cts.Token);
        for (var i = 0; i < 2; i++)
        {
            var periodic = await PacketFramer.ReadAsync(client, Direction.ServerToClient, cts.Token);
            Assert.Equal(333u, PacketDecoder.Decode(periodic!)["requestId"]);
        }

        await PacketFramer.WriteAsync(client, TestPackets.RequestDataOnSimObject(333, 17, period: 0), cts.Token);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task Fake_server_replays_recorded_facility_data()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // A pass-through recording in which request 7 for YBBN got two FacilityData packets.
        var source = Path.Combine(_dir, "source.jsonl");
        using (var sourceRecorder = new JsonlRecorder(source))
        {
            sourceRecorder.Record(TestPackets.RequestFacilityData(444, 7, "YBBN"), 1);
            sourceRecorder.Record(FacilityData(7, 0xAA), 1);
            sourceRecorder.Record(FacilityData(7, 0xBB), 1);
            sourceRecorder.Record(PacketBuilder.Server(RecvId.FacilityDataEnd, TestPackets.SunRise).U32(7).Build(), 1);
        }

        var replay = FacilityReplay.FromRecording(source);
        using var recorder = new JsonlRecorder(Path.Combine(_dir, "replay.jsonl"));
        await using var listener = Transports.Listen(Endpoint.Parse("tcp:0"));
        _ = new FakeSimServer(listener, ServerIdentity.Msfs2024, recorder, facilities: replay).RunAsync(cts.Token);
        await using var client = await Transports.ConnectAsync(listener.Endpoint, cts.Token);

        await PacketFramer.WriteAsync(client, TestPackets.RequestFacilityData(444, 1, "YBBN"), cts.Token);
        var first = await PacketFramer.ReadAsync(client, Direction.ServerToClient, cts.Token);
        var second = await PacketFramer.ReadAsync(client, Direction.ServerToClient, cts.Token);
        var end = await PacketFramer.ReadAsync(client, Direction.ServerToClient, cts.Token);

        Assert.Equal(1, replay.Count);
        Assert.Equal(1u, PacketDecoder.Decode(first!)["userRequestId"]);
        Assert.Equal("AA", PacketDecoder.Decode(first!)["dataHex"]);
        Assert.Equal("BB", PacketDecoder.Decode(second!)["dataHex"]);
        Assert.Equal(RecvId.FacilityDataEnd, (RecvId)end!.TypeField);
        Assert.Equal(1u, PacketDecoder.Decode(end)["requestId"]);

        // An airport, or a definition, that was not recorded gets exception 1 with the request's send id.
        await PacketFramer.WriteAsync(client, TestPackets.RequestFacilityData(444, 2, "YSAM", sendId: 99), cts.Token);
        var missing = PacketDecoder.Decode((await PacketFramer.ReadAsync(client, Direction.ServerToClient, cts.Token))!);
        Assert.Equal(1u, missing["exception"]);
        Assert.Equal(99u, missing["sendId"]);
        await cts.CancelAsync();

        static Packet FacilityData(uint requestId, byte data) =>
            PacketBuilder.Server(RecvId.FacilityData, TestPackets.SunRise)
                .U32(requestId).U32(0).U32(0).U32(0).U32(0).U32(uint.MaxValue).U32(uint.MaxValue).U8(data)
                .Build();
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

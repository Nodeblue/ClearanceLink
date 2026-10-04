using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using ClearanceLink.Host;
using ClearanceLink.Host.Status;
using ClearanceLink.Host.Transport;
using ClearanceLink.Protocol;
using ClearanceLink.Protocol.Tests;
using ClearanceLink.Recorder;
using ClearanceLink.Server;

namespace ClearanceLink.Host.Tests;

public sealed class StatusTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("clearancelink-tests").FullName;

    public StatusTests() => PacketConsole.Verbose = false;

    [Theory]
    [InlineData(false, new string[0], StatusLevel.Waiting, "Waiting for X-Plane")]
    [InlineData(true, new string[0], StatusLevel.Waiting, "Start BeyondATC")]
    [InlineData(true, new[] { "BeyondATC" }, StatusLevel.Waiting, "still connecting")]
    [InlineData(true, new[] { "BeyondATC", "BATCDLLSimConnection" }, StatusLevel.Ok, "Ready")]
    [InlineData(false, new[] { "BeyondATC", "BATCDLLSimConnection" }, StatusLevel.Problem, "X-Plane is not")]
    public void Overall_names_the_next_thing_to_start(bool xplaneConnected, string[] clients, StatusLevel level, string message)
    {
        var sessions = clients.Select((name, i) => OpenSession(i + 1, name)).ToList();

        var overall = StatusBoard.Overall(new XPlaneStatus(xplaneConnected, null, "http://127.0.0.1:8086/"), sessions);

        Assert.Equal(level, overall.Level);
        Assert.Contains(message, overall.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Overall_says_to_close_MSFS_while_it_holds_the_default_connection()
    {
        var xp = new XPlaneStatus(true, null, "http://127.0.0.1:8086/");

        var blocked = StatusBoard.Overall(xp, [OpenSession(1, "FS2024BridgeService")], msfsRunning: true);
        var connected = StatusBoard.Overall(xp, [OpenSession(1, "BeyondATC"), OpenSession(2, "BATCDLLSimConnection")], msfsRunning: true);

        Assert.Equal(StatusLevel.Problem, blocked.Level);
        Assert.Contains("Close it", blocked.Message, StringComparison.Ordinal);
        Assert.Equal(StatusLevel.Ok, connected.Level);
    }

    [Fact]
    public async Task Status_page_shows_live_connections_and_airports_sent()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var xp = new InMemoryXPlane();
        var mappings = new MappingSet([], []);
        using var recorder = new JsonlRecorder(Path.Combine(_dir, "serve.jsonl"));
        await using var listener = Transports.Listen(Endpoint.Parse("tcp:0"));
        var server = new XPlaneSimServer(listener, ServerIdentity.Msfs2024, recorder, new SimVarResolver(mappings, xp), xp, new OnlyAirport("YSSY"));
        var run = server.RunAsync(cts.Token);
        var board = new StatusBoard(() => server.Sessions, () => new XPlaneStatus(xp.IsConnected, "12.4.3", "http://127.0.0.1:8086/"), listener.Endpoint.ToString())
        {
            Stub = "started",
        };
        var port = FreePort();
        await using var status = new StatusServer(board, port, _ => { });
        Assert.True(status.Start(cts.Token));

        await using var helper = await Transports.ConnectAsync(listener.Endpoint, cts.Token);
        await PacketFramer.WriteAsync(helper, TestPackets.Open("BeyondATC"), cts.Token);
        await PacketFramer.WriteAsync(helper, TestPackets.RequestFacilityData(1, 10, "YSSY"), cts.Token);
        await PacketFramer.WriteAsync(helper, TestPackets.RequestFacilityData(1, 11, "YXXX"), cts.Token);
        await using var app = await Transports.ConnectAsync(listener.Endpoint, cts.Token);
        await PacketFramer.WriteAsync(app, TestPackets.Open("BATCDLLSimConnection"), cts.Token);

        using var http = new HttpClient();
        StatusSnapshot? snapshot = null;
        while (snapshot is not { Airports.Count: 2, Clients.Count: 2 } || snapshot.Clients.Any(c => c.Name == ""))
        {
            await Task.Delay(50, cts.Token);
            snapshot = await http.GetFromJsonAsync<StatusSnapshot>($"{status.Url}status.json", StatusBoard.Json, cts.Token);
        }

        Assert.Equal(StatusLevel.Ok, snapshot.Overall.Level);
        Assert.Equal(["BeyondATC", "BATCDLLSimConnection"], snapshot.Clients.Select(c => c.Name));
        Assert.Equal("YXXX", snapshot.Airports[0].Icao);
        Assert.Null(snapshot.Airports[0].Source);
        Assert.Equal(("YSSY", "test airports"), (snapshot.Airports[1].Icao, snapshot.Airports[1].Source));
        Assert.Equal("12.4.3", snapshot.XPlane.Version);

        var page = await http.GetAsync(status.Url, cts.Token);
        Assert.Equal("text/html; charset=utf-8", page.Content.Headers.ContentType?.ToString());
        Assert.Contains("status.json", await page.Content.ReadAsStringAsync(cts.Token), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"{status.Url}nope", cts.Token)).StatusCode);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task Port_in_use_leaves_clearancelink_running_without_the_page()
    {
        var port = FreePort();
        var lines = new List<string>();
        var board = new StatusBoard(() => [], () => new XPlaneStatus(false, null, "x"), "tcp:5112");
        await using var first = new StatusServer(board, port, _ => { });
        Assert.True(first.Start(CancellationToken.None));
        await using var second = new StatusServer(board, port, lines.Add);

        Assert.False(second.Start(CancellationToken.None));
        Assert.Contains(lines, l => l.StartsWith("status: page not available", StringComparison.Ordinal));
    }

    [Fact]
    public void Snapshot_json_uses_lower_case_names_for_the_page()
    {
        var board = new StatusBoard(() => [], () => new XPlaneStatus(true, "12.4.3", "x"), "tcp:5112");

        using var json = JsonDocument.Parse(board.ToJson());

        Assert.Equal("waiting", json.RootElement.GetProperty("overall").GetProperty("level").GetString());
        Assert.True(json.RootElement.GetProperty("xPlane").GetProperty("connected").GetBoolean());
        Assert.False(json.RootElement.TryGetProperty("plugin", out _));

        board.Plugin = () => new ClearanceLink.Host.Traffic.PluginStatus("0.1.0", 3, 900, ["D:\\Bluebell"], DateTimeOffset.UtcNow);
        using var withPlugin = JsonDocument.Parse(board.ToJson());
        Assert.Equal(900, withPlugin.RootElement.GetProperty("plugin").GetProperty("models").GetInt32());
    }

    private static SimConnectSession OpenSession(int id, string name)
    {
        var session = new SimConnectSession(id, ServerIdentity.Msfs2024, new SimVarResolver(new MappingSet([], []), new InMemoryXPlane()), new InMemoryXPlane(), _ => Task.CompletedTask, _ => { });
        session.HandleAsync(TestPackets.Open(name)).GetAwaiter().GetResult();
        return session;
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Windows may still hold the recording briefly.
        }
    }

    private sealed class OnlyAirport(string icao) : IFacilitySource
    {
        public string Description => "test airports";

        public IReadOnlyList<Packet>? Find(FacilityRequest request) => request.Icao == icao ? [] : null;
    }
}

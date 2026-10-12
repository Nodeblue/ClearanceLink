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
using static ClearanceLink.Host.Status.KnownClients;

namespace ClearanceLink.Host.Tests;

public sealed class StatusTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("clearancelink-tests").FullName;

    public StatusTests() => PacketConsole.Verbose = false;

    [Theory]
    [InlineData(false, new string[0], StatusLevel.Waiting, "Waiting for X-Plane")]
    [InlineData(true, new string[0], StatusLevel.Waiting, "Start the ATC app")]
    [InlineData(true, new[] { HelperAirports }, StatusLevel.Waiting, "still connecting")]
    [InlineData(true, new[] { HelperAirports, AircraftAndRadios }, StatusLevel.Ok, "Ready")]
    [InlineData(false, new[] { HelperAirports, AircraftAndRadios }, StatusLevel.Problem, "X-Plane is not")]
    [InlineData(true, new[] { AirportData, TrafficTiming }, StatusLevel.Waiting, "still connecting")]
    [InlineData(true, new[] { AircraftAndRadios, AirportData, TrafficTiming }, StatusLevel.Ok, "Ready")]
    [InlineData(true, new[] { AircraftAndRadios, AirportData }, StatusLevel.Ok, "Ready")]
    [InlineData(false, new[] { TrafficTiming }, StatusLevel.Problem, "X-Plane is not")]
    [InlineData(true, new[] { "Some Other ATC" }, StatusLevel.Ok, "Ready")]
    public void Overall_names_the_next_thing_to_start(bool xplaneConnected, string[] clients, StatusLevel level, string message)
    {
        var sessions = clients.Select((name, i) => OpenSession(i + 1, name)).ToList();

        var overall = StatusBoard.Overall(new XPlaneStatus(xplaneConnected, null, "http://127.0.0.1:8086/"), sessions);

        Assert.Equal(level, overall.Level);
        Assert.Contains(message, overall.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { HelperAirports, AircraftAndRadios }, true)]
    [InlineData(new[] { AircraftAndRadios, AirportData, TrafficTiming }, true)]
    [InlineData(new[] { AircraftAndRadios, TrafficTiming }, false)]
    [InlineData(new[] { AirportData, TrafficTiming }, false)]
    [InlineData(new[] { AircraftAndRadios, "FS2024BridgeService" }, false)]
    [InlineData(new[] { "Some Other ATC" }, true)]
    [InlineData(new[] { "" }, false)]
    [InlineData(new string[0], false)]
    public void The_recorded_app_needs_its_main_and_airport_connections_and_any_other_app_one(string[] names, bool connected)
    {
        Assert.Equal(connected, StatusBoard.AppConnected(names));
    }

    [Fact]
    public void On_msfss_default_connection_other_add_ons_are_not_the_app()
    {
        // There any MSFS add-on connects (SPAD.neXt), so only recorded names count and the stand-in stays.
        Assert.False(StatusBoard.AppConnected(["SPAD.neXt"], anyName: false));
        Assert.Equal(0, Open(["SPAD.neXt", "Some Other ATC"], anyName: false));
        Assert.True(StatusBoard.AppConnected([HelperAirports, AircraftAndRadios], anyName: false));
        var xp = new XPlaneStatus(true, null, "http://127.0.0.1:8086/");
        Assert.Contains("Start the ATC app", StatusBoard.Overall(xp, [OpenSession(1, "SPAD.neXt")], anyName: false).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HelperAirports, true)]
    [InlineData(AircraftAndRadios, true)]
    [InlineData(AirportData, true)]
    [InlineData(TrafficTiming, true)]
    [InlineData("FS2024BridgeService", false)]
    [InlineData("SPAD.neXt", false)]
    public void Recorded_connection_names_are_known(string name, bool known)
    {
        Assert.Equal(known, IsKnown(name));
    }

    [Fact]
    public async Task The_recorded_apps_1_8_22_shows_ready_with_its_three_connections()
    {
        // The app's 1.8.22 dropped the app's helper: The app's .exe opens all three (recorded on Ben's PC, 2026-10-07).
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var xp = new InMemoryXPlane();
        using var recorder = new JsonlRecorder(Path.Combine(_dir, "serve.jsonl"));
        await using var listener = Transports.Listen(Endpoint.Parse("tcp:0"));
        var server = new XPlaneSimServer(listener, ServerIdentity.Msfs2024, recorder, new SimVarResolver(new MappingSet([], []), xp), xp, new OnlyAirport("YSSY"));
        var run = server.RunAsync(cts.Token);
        var board = new StatusBoard(() => server.Sessions, () => new XPlaneStatus(true, "12.4.3", "http://127.0.0.1:8086/"), listener.Endpoint.ToString());

        await using var app = await Transports.ConnectAsync(listener.Endpoint, cts.Token);
        await PacketFramer.WriteAsync(app, TestPackets.Open(AircraftAndRadios), cts.Token);
        await using var facilities = await Transports.ConnectAsync(listener.Endpoint, cts.Token);
        await PacketFramer.WriteAsync(facilities, TestPackets.Open(AirportData), cts.Token);
        await PacketFramer.WriteAsync(facilities, TestPackets.RequestFacilityData(1, 10, "YSSY"), cts.Token);
        await using var sync = await Transports.ConnectAsync(listener.Endpoint, cts.Token);
        await PacketFramer.WriteAsync(sync, TestPackets.Open(TrafficTiming), cts.Token);

        var snapshot = board.Snapshot();
        while (snapshot is not { Airports.Count: 1, Clients.Count: 3 } || snapshot.Clients.Any(c => c.Name == ""))
        {
            await Task.Delay(50, cts.Token);
            snapshot = board.Snapshot();
        }

        Assert.Equal(StatusLevel.Ok, snapshot.Overall.Level);
        Assert.All(snapshot.Clients, c => Assert.StartsWith("ATC app", c.Description, StringComparison.Ordinal));
        Assert.Equal("YSSY", snapshot.Airports[0].Icao);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public void Overall_says_to_close_MSFS_while_it_holds_the_default_connection()
    {
        var xp = new XPlaneStatus(true, null, "http://127.0.0.1:8086/");

        var blocked = StatusBoard.Overall(xp, [OpenSession(1, "FS2024BridgeService")], msfsRunning: true);
        var connected = StatusBoard.Overall(xp, [OpenSession(1, HelperAirports), OpenSession(2, AircraftAndRadios)], msfsRunning: true);

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
        await PacketFramer.WriteAsync(helper, TestPackets.Open(HelperAirports), cts.Token);
        await PacketFramer.WriteAsync(helper, TestPackets.RequestFacilityData(1, 10, "YSSY"), cts.Token);
        await PacketFramer.WriteAsync(helper, TestPackets.RequestFacilityData(1, 11, "YXXX"), cts.Token);
        await using var app = await Transports.ConnectAsync(listener.Endpoint, cts.Token);
        await PacketFramer.WriteAsync(app, TestPackets.Open(AircraftAndRadios), cts.Token);

        using var http = new HttpClient();
        StatusSnapshot? snapshot = null;
        while (snapshot is not { Airports.Count: 2, Clients.Count: 2 } || snapshot.Clients.Any(c => c.Name == ""))
        {
            await Task.Delay(50, cts.Token);
            snapshot = await http.GetFromJsonAsync<StatusSnapshot>($"{status.Url}status.json", StatusBoard.Json, cts.Token);
        }

        Assert.Equal(StatusLevel.Ok, snapshot.Overall.Level);
        Assert.Equal([HelperAirports, AircraftAndRadios], snapshot.Clients.Select(c => c.Name));
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

using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClearanceLink.XPlane;

namespace ClearanceLink.Server.Tests;

/// <summary>
/// Runs the client against a small fake of X-Plane's Web API v2 (REST lookups, WebSocket subscribe and updates,
/// PATCH writes, command activation), shaped after the X-Plane 12 Web API documentation.
/// </summary>
public sealed class XPlaneWebApiClientTests : IDisposable
{
    private readonly FakeWebApi _api = new();

    [Fact]
    public async Task Subscribes_to_watched_datarefs_and_reads_pushed_values()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var client = new XPlaneWebApiClient(_api.BaseUri, _ => { });
        client.Watch("sim/flightmodel/position/latitude");
        client.Watch("sim/flightmodel2/engines/engine_is_burning_fuel[1]");
        client.Watch("sim/aircraft/view/acf_ICAO");
        client.Watch("sim/does/not/exist");
        var run = client.RunAsync(cts.Token);

        await WaitForAsync(() => client.TryGetNumber("sim/flightmodel/position/latitude", out _), cts.Token);

        Assert.True(client.IsConnected);
        Assert.Equal("12.4.0", client.XPlaneVersion);
        Assert.True(client.TryGetNumber("sim/flightmodel/position/latitude", out var lat));
        Assert.Equal(-27.4016, lat);
        Assert.True(client.TryGetNumber("sim/flightmodel2/engines/engine_is_burning_fuel[1]", out var burning));
        Assert.Equal(1, burning);
        Assert.True(client.TryGetText("sim/aircraft/view/acf_ICAO", out var icao));
        Assert.Equal("SF50", icao);
        Assert.Contains("sim/does/not/exist", client.Missing);

        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Only_data_datarefs_are_base64_decoded()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var client = new XPlaneWebApiClient(_api.BaseUri, _ => { });
        client.Watch("test/plain/text");
        client.Watch("sim/aircraft/view/acf_ICAO");
        var run = client.RunAsync(cts.Token);

        await WaitForAsync(() => client.TryGetText("test/plain/text", out _) && client.TryGetText("sim/aircraft/view/acf_ICAO", out _), cts.Token);

        Assert.True(client.TryGetText("test/plain/text", out var plain));
        Assert.Equal("ABCD", plain); // valid base64, but not a data dataref: passed on as it is
        Assert.True(client.TryGetText("sim/aircraft/view/acf_ICAO", out var icao));
        Assert.Equal("SF50", icao);

        await cts.CancelAsync();
        await run;
    }

    [Theory]
    [InlineData(0, 60, false)] // nothing subscribed: silence is expected
    [InlineData(3, 9, false)]
    [InlineData(3, 10, true)]
    [InlineData(3, 60, true)]
    public void Liveness_probe_is_needed_only_when_subscribed_and_quiet(int subscribed, int quietSeconds, bool expected) =>
        Assert.Equal(expected, XPlaneWebApiClient.NeedsLivenessProbe(subscribed, TimeSpan.FromSeconds(quietSeconds), TimeSpan.FromSeconds(10)));

    [Fact]
    public async Task A_quiet_socket_while_X_Plane_answers_stays_connected()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var client = new XPlaneWebApiClient(_api.BaseUri, log.Enqueue) { StaleAfter = TimeSpan.FromMilliseconds(300) };
        client.Watch("sim/flightmodel/position/latitude");
        var run = client.RunAsync(cts.Token);

        await WaitForAsync(() => log.Any(l => l.Contains("keeping the connection", StringComparison.Ordinal)), cts.Token);
        await Task.Delay(1000, cts.Token);

        Assert.True(client.IsConnected);
        Assert.True(client.LastUpdateAge >= TimeSpan.FromMilliseconds(300));
        Assert.Single(log, l => l.Contains("keeping the connection", StringComparison.Ordinal)); // once per quiet spell
        Assert.DoesNotContain(log, l => l.Contains("DISCONNECTED", StringComparison.Ordinal));

        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task A_quiet_socket_while_X_Plane_does_not_answer_is_a_disconnect()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var client = new XPlaneWebApiClient(_api.BaseUri, log.Enqueue) { StaleAfter = TimeSpan.FromMilliseconds(300) };
        client.Watch("sim/flightmodel/position/latitude");
        var run = client.RunAsync(cts.Token);
        await WaitForAsync(() => client.TryGetNumber("sim/flightmodel/position/latitude", out _), cts.Token);

        _api.RestFails = true; // X-Plane hangs: the socket stays open but nothing answers

        await WaitForAsync(() => !client.IsConnected, cts.Token);
        Assert.Contains(log, l => l.StartsWith("xplane DISCONNECTED: no dataref update", StringComparison.Ordinal));
        Assert.Null(client.LastUpdateAge);

        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Writes_datarefs_and_runs_commands_over_rest()
    {
        using var client = new XPlaneWebApiClient(_api.BaseUri, _ => { });

        await client.WriteAsync("sim/cockpit2/radios/actuators/com1_frequency_hz_833", 118850);
        await client.CommandAsync("sim/radios/com1_standy_flip");

        Assert.Equal(["PATCH /api/v2/datarefs/5/value {\"data\":118850}", "POST /api/v2/command/77/activate {\"duration\":0}"], _api.Calls);
    }

    [Fact]
    public async Task Command_presence_is_asked_fresh_each_time()
    {
        using var client = new XPlaneWebApiClient(_api.BaseUri, _ => { });

        Assert.False(await client.HasCommandAsync("BetterPushback/start"));
        _api.ExtraCommands["BetterPushback/start"] = 3138; // the plugin is loaded later
        Assert.True(await client.HasCommandAsync("BetterPushback/start"));
        Assert.True(await client.HasCommandAsync("sim/radios/com1_standy_flip"));
    }

    public void Dispose() => _api.Dispose();

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            await Task.Delay(20, ct);
        }
    }

    private sealed class FakeWebApi : IDisposable
    {
        private static readonly (long Id, string Name, string Type, JsonNode Value)[] Datarefs =
        [
            (1, "sim/flightmodel/position/latitude", "double", -27.4016),
            (2, "sim/flightmodel2/engines/engine_is_burning_fuel", "int_array", new JsonArray(0, 1)),
            (3, "sim/aircraft/view/acf_ICAO", "data", Convert.ToBase64String(Encoding.ASCII.GetBytes("SF50\0\0\0\0"))),
            (4, "test/plain/text", "string", "ABCD"),
            (5, "sim/cockpit2/radios/actuators/com1_frequency_hz_833", "int", 127850),
        ];

        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();

        public FakeWebApi()
        {
            var port = FreePort();
            BaseUri = new Uri($"http://127.0.0.1:{port}/");
            _listener.Prefixes.Add(BaseUri.ToString());
            _listener.Start();
            _ = Task.Run(ServeAsync);
        }

        public Uri BaseUri { get; }

        public List<string> Calls { get; } = [];

        public volatile bool RestFails;

        /// <summary>Plugin commands the fake has, besides the radio one; may change while a test runs.</summary>
        public System.Collections.Concurrent.ConcurrentDictionary<string, long> ExtraCommands { get; } = new();

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Close();
            _stop.Dispose();
        }

        private async Task ServeAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(context));
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var path = request.Url!.AbsolutePath;
            if (request.IsWebSocketRequest && path == "/api/v2")
            {
                var ws = (await context.AcceptWebSocketAsync(null)).WebSocket;
                await WebSocketAsync(ws);
                return;
            }

            var body = await new StreamReader(request.InputStream).ReadToEndAsync();
            if (RestFails)
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
                return;
            }

            JsonNode response = new JsonObject();
            switch (request.HttpMethod, path)
            {
                case ("GET", "/api/capabilities"):
                    response = new JsonObject { ["api"] = new JsonObject { ["versions"] = new JsonArray("v1", "v2") }, ["x-plane"] = new JsonObject { ["version"] = "12.4.0" } };
                    break;
                case ("GET", "/api/v2/datarefs"):
                    var name = request.QueryString["filter[name]"];
                    response = new JsonObject
                    {
                        ["data"] = new JsonArray(Datarefs.Where(d => d.Name == name)
                            .Select(d => (JsonNode)new JsonObject { ["id"] = d.Id, ["name"] = d.Name, ["value_type"] = d.Type })
                            .ToArray()),
                    };
                    break;
                case ("GET", "/api/v2/commands"):
                    var command = request.QueryString["filter[name]"] ?? "";
                    long? commandId = command == "sim/radios/com1_standy_flip" ? 77 : ExtraCommands.TryGetValue(command, out var extra) ? extra : null;
                    response = new JsonObject
                    {
                        ["data"] = commandId is { } found
                            ? new JsonArray(new JsonObject { ["id"] = found, ["name"] = command })
                            : new JsonArray(),
                    };
                    break;
                default:
                    lock (Calls)
                    {
                        Calls.Add($"{request.HttpMethod} {path} {body}");
                    }

                    break;
            }

            var bytes = Encoding.UTF8.GetBytes(response.ToJsonString());
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }

        private async Task WebSocketAsync(WebSocket ws)
        {
            var buffer = new byte[16 * 1024];
            while (ws.State == WebSocketState.Open && !_stop.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await ws.ReceiveAsync(buffer, _stop.Token);
                }
                catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
                {
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                using var doc = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
                var root = doc.RootElement;
                await SendAsync(ws, new JsonObject { ["req_id"] = root.GetProperty("req_id").GetInt32(), ["type"] = "result", ["success"] = true });
                if (root.GetProperty("type").GetString() == "dataref_subscribe_values")
                {
                    var data = new JsonObject();
                    foreach (var item in root.GetProperty("params").GetProperty("datarefs").EnumerateArray())
                    {
                        var id = item.GetProperty("id").GetInt64();
                        data[id.ToString(System.Globalization.CultureInfo.InvariantCulture)] = Datarefs.Single(d => d.Id == id).Value.DeepClone();
                    }

                    await SendAsync(ws, new JsonObject { ["type"] = "dataref_update_values", ["data"] = data });
                }
            }
        }

        private static Task SendAsync(WebSocket ws, JsonNode message) =>
            ws.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);

        private static int FreePort()
        {
            using var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            return ((IPEndPoint)socket.LocalEndpoint).Port;
        }
    }
}

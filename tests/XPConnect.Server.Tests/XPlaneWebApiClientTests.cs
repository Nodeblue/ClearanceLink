using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using XPConnect.XPlane;

namespace XPConnect.Server.Tests;

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
    public async Task Writes_datarefs_and_runs_commands_over_rest()
    {
        using var client = new XPlaneWebApiClient(_api.BaseUri, _ => { });

        await client.WriteAsync("sim/cockpit2/radios/actuators/com1_frequency_hz_833", 118850);
        await client.CommandAsync("sim/radios/com1_standy_flip");

        Assert.Equal(["PATCH /api/v2/datarefs/5/value {\"data\":118850}", "POST /api/v2/command/77/activate {\"duration\":0}"], _api.Calls);
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
                    response = new JsonObject
                    {
                        ["data"] = request.QueryString["filter[name]"] == "sim/radios/com1_standy_flip"
                            ? new JsonArray(new JsonObject { ["id"] = 77, ["name"] = "sim/radios/com1_standy_flip" })
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

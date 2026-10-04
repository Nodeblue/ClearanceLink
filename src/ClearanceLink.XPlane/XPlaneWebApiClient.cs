using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClearanceLink.Server;

namespace ClearanceLink.XPlane;

/// <summary>
/// Talks to X-Plane 12's built-in Web API (REST + WebSocket, default http://127.0.0.1:8086, API v2, X-Plane 12.1.4+).
/// Watched datarefs are looked up by name over REST and subscribed over the WebSocket; X-Plane pushes changes and
/// reads are answered from the cache. Writes and commands go over REST. Reconnects on its own when X-Plane restarts.
/// </summary>
public sealed class XPlaneWebApiClient : IXPlaneData, IDisposable
{
    private readonly Uri _baseUri;
    private readonly HttpClient _http;
    private readonly Action<string> _log;
    private readonly ConcurrentDictionary<string, byte> _watched = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DatarefInfo?> _datarefs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long?> _commands = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, JsonElement> _values = new();
    private readonly ConcurrentDictionary<long, byte> _subscribed = new();
    private readonly SemaphoreSlim _newWatches = new(0);
    private int _requestId;
    private volatile bool _connected;

    public XPlaneWebApiClient(Uri baseUri, Action<string> log, HttpMessageHandler? handler = null)
    {
        _baseUri = baseUri;
        _log = log;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = baseUri;
        _http.Timeout = TimeSpan.FromSeconds(5);
    }

    public bool IsConnected => _connected;

    /// <summary>X-Plane version reported by the API on connect, e.g. "12.4.0".</summary>
    public string? XPlaneVersion { get; private set; }

    /// <summary>Raised when the connection comes up or drops.</summary>
    public event Action<bool>? ConnectionChanged;

    public IReadOnlyCollection<string> Missing =>
        _datarefs.Where(kv => kv.Value is null).Select(kv => kv.Key).Concat(_commands.Where(kv => kv.Value is null).Select(kv => kv.Key)).ToList();

    public void Watch(string dataref)
    {
        if (_watched.TryAdd(DatarefName.Parse(dataref).Name, 0))
        {
            _newWatches.Release();
        }
    }

    public bool TryGetNumber(string dataref, out double value)
    {
        value = 0;
        var name = DatarefName.Parse(dataref);
        if (!TryGetRaw(name.Name, out var raw))
        {
            return false;
        }

        if (raw.ValueKind == JsonValueKind.Array)
        {
            var i = name.Index ?? 0;
            if (i >= raw.GetArrayLength())
            {
                return false;
            }

            raw = raw[i];
        }

        switch (raw.ValueKind)
        {
            case JsonValueKind.Number:
                value = raw.GetDouble();
                return true;
            case JsonValueKind.True or JsonValueKind.False:
                value = raw.GetBoolean() ? 1 : 0;
                return true;
            default:
                return false;
        }
    }

    public bool TryGetText(string dataref, out string value)
    {
        value = "";
        if (!TryGetRaw(DatarefName.Parse(dataref).Name, out var raw) || raw.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        // "data" datarefs arrive base64-encoded and NUL-padded.
        try
        {
            var bytes = Convert.FromBase64String(raw.GetString() ?? "");
            var end = Array.IndexOf(bytes, (byte)0);
            value = Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end);
        }
        catch (FormatException)
        {
            value = raw.GetString() ?? "";
        }

        return true;
    }

    public async Task WriteAsync(string dataref, double value, CancellationToken ct = default)
    {
        var name = DatarefName.Parse(dataref);
        var info = await LookupDatarefAsync(name.Name, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"X-Plane has no dataref {name.Name}");
        var url = $"api/v2/datarefs/{info.Id}/value" + (name.Index is { } i ? $"?index={i}" : "");
        JsonNode data = info.ValueType.StartsWith("int", StringComparison.Ordinal) ? (long)Math.Round(value) : value;
        using var response = await _http.PatchAsJsonAsync(url, new JsonObject { ["data"] = data }, ct).ConfigureAwait(false);
        await EnsureOkAsync(response, $"write {dataref}", ct).ConfigureAwait(false);
    }

    public async Task CommandAsync(string command, CancellationToken ct = default)
    {
        var id = await LookupCommandAsync(command, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"X-Plane has no command {command}");
        using var response = await _http.PostAsJsonAsync($"api/v2/command/{id}/activate", new JsonObject { ["duration"] = 0 }, ct).ConfigureAwait(false);
        await EnsureOkAsync(response, $"command {command}", ct).ConfigureAwait(false);
    }

    /// <summary>Connects, keeps the subscription up to date and reconnects until cancelled.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var announcedWaiting = false;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(ct).ConfigureAwait(false);
                announcedWaiting = false;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or WebSocketException or IOException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                if (_connected)
                {
                    _log($"xplane DISCONNECTED: {ex.Message}");
                }
                else if (!announcedWaiting)
                {
                    _log($"xplane waiting for X-Plane Web API at {_baseUri} ({ex.Message})");
                    announcedWaiting = true;
                }
            }

            SetConnected(false);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        SetConnected(false);
    }

    public void Dispose()
    {
        _http.Dispose();
        _newWatches.Dispose();
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        // Dataref ids are only valid for one X-Plane run, so look everything up again on each connection.
        _datarefs.Clear();
        _commands.Clear();
        _values.Clear();
        _subscribed.Clear();

        var capabilities = await _http.GetFromJsonAsync<JsonElement>("api/capabilities", ct).ConfigureAwait(false);
        XPlaneVersion = capabilities.TryGetProperty("x-plane", out var xp) && xp.TryGetProperty("version", out var v) ? v.GetString() : null;

        using var socket = new ClientWebSocket();
        var wsUri = new UriBuilder(_baseUri) { Scheme = _baseUri.Scheme == "https" ? "wss" : "ws", Path = "api/v2" }.Uri;
        await socket.ConnectAsync(wsUri, ct).ConfigureAwait(false);
        _log($"xplane CONNECTED to X-Plane {XPlaneVersion ?? "(unknown version)"} at {_baseUri}");
        SetConnected(true);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var receive = ReceiveLoopAsync(socket, linked.Token);
        var subscribe = SubscribeLoopAsync(socket, linked.Token);
        var first = await Task.WhenAny(receive, subscribe).ConfigureAwait(false);
        await linked.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(receive, subscribe).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The other loop ended; surface its error below.
        }

        await first.ConfigureAwait(false);
        throw new IOException("X-Plane closed the Web API connection");
    }

    private async Task SubscribeLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var batch = new JsonArray();
            foreach (var name in _watched.Keys)
            {
                if (await LookupDatarefAsync(name, ct).ConfigureAwait(false) is { } info && _subscribed.TryAdd(info.Id, 0))
                {
                    batch.Add(new JsonObject { ["id"] = info.Id });
                }
            }

            if (batch.Count > 0)
            {
                var message = new JsonObject
                {
                    ["req_id"] = Interlocked.Increment(ref _requestId),
                    ["type"] = "dataref_subscribe_values",
                    ["params"] = new JsonObject { ["datarefs"] = batch },
                };
                await socket.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                _log($"xplane subscribed to {batch.Count} dataref(s), {_subscribed.Count} in total");
            }

            await _newWatches.WaitAsync(ct).ConfigureAwait(false);
            await Task.Delay(50, ct).ConfigureAwait(false); // let a burst of Watch calls collect into one request
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        while (!ct.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            HandleMessage(message.ToArray());
            message.SetLength(0);
        }
    }

    private void HandleMessage(byte[] json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
        switch (type)
        {
            case "dataref_update_values" when root.TryGetProperty("data", out var data):
                foreach (var entry in data.EnumerateObject())
                {
                    if (long.TryParse(entry.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                    {
                        _values[id] = entry.Value.Clone();
                    }
                }

                break;
            case "result" when root.TryGetProperty("success", out var ok) && !ok.GetBoolean():
                _log($"xplane request {root.GetProperty("req_id")} failed: {root.GetRawText()}");
                break;
        }
    }

    private bool TryGetRaw(string name, out JsonElement raw)
    {
        raw = default;
        return _datarefs.TryGetValue(name, out var info) && info is not null && _values.TryGetValue(info.Id, out raw);
    }

    private async Task<DatarefInfo?> LookupDatarefAsync(string name, CancellationToken ct)
    {
        if (_datarefs.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var found = await FindAsync("api/v2/datarefs", name, ct).ConfigureAwait(false);
        var info = found is { } e ? new DatarefInfo(e.GetProperty("id").GetInt64(), e.TryGetProperty("value_type", out var vt) ? vt.GetString() ?? "" : "") : null;
        if (info is null)
        {
            _log($"xplane NOT FOUND dataref {name} (check mapping/*.yaml)");
        }

        _datarefs[name] = info;
        return info;
    }

    private async Task<long?> LookupCommandAsync(string name, CancellationToken ct)
    {
        if (_commands.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var found = await FindAsync("api/v2/commands", name, ct).ConfigureAwait(false);
        long? id = found is { } e ? e.GetProperty("id").GetInt64() : null;
        if (id is null)
        {
            _log($"xplane NOT FOUND command {name} (check mapping/events.yaml)");
        }

        _commands[name] = id;
        return id;
    }

    private async Task<JsonElement?> FindAsync(string collection, string name, CancellationToken ct)
    {
        var body = await _http.GetFromJsonAsync<JsonElement>($"{collection}?filter[name]={Uri.EscapeDataString(name)}", ct).ConfigureAwait(false);
        if (body.TryGetProperty("data", out var data))
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var n) && n.GetString() == name)
                {
                    return item.Clone();
                }
            }
        }

        return null;
    }

    private static async Task EnsureOkAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"X-Plane refused {what}: {(int)response.StatusCode} {text}");
        }
    }

    private void SetConnected(bool connected)
    {
        if (_connected != connected)
        {
            _connected = connected;
            ConnectionChanged?.Invoke(connected);
        }
    }

    private sealed record DatarefInfo(long Id, string ValueType);
}

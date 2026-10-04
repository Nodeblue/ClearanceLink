using System.Net;
using System.Text;

namespace ClearanceLink.Host.Status;

/// <summary>
/// Serves the status page on http://localhost:&lt;port&gt;/ and its data on /status.json. Local only. If the port is
/// taken it logs that and ClearanceLink carries on without the page.
/// </summary>
public sealed class StatusServer(StatusBoard board, int port, Action<string> log) : IAsyncDisposable
{
    public const int DefaultPort = 5577;

    private readonly HttpListener _listener = new();
    private Task? _loop;

    public string Url => $"http://localhost:{port}/";

    public bool Start(CancellationToken ct)
    {
        _listener.Prefixes.Add(Url);
        try
        {
            _listener.Start();
        }
        catch (Exception ex) when (ex is HttpListenerException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            log($"status: page not available, port {port} in use or blocked ({ex.Message}); use --status-port <port>");
            return false;
        }

        log($"status: {Url}");
        _loop = Task.Run(() => LoopAsync(ct), ct);
        return true;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        using var registration = ct.Register(() => _listener.Stop());
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            // Stopped before or while asking for the next request. On Windows a listener stopped just before
            // GetContextAsync throws ArgumentException ("The handle is invalid").
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException
                                       || (ex is ArgumentException && !_listener.IsListening))
            {
                return;
            }

            try
            {
                await RespondAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
            {
                // The browser went away mid-response.
            }
        }
    }

    private async Task RespondAsync(HttpListenerContext context)
    {
        var response = context.Response;
        var path = context.Request.Url?.AbsolutePath ?? "/";
        var (status, type, body) = path switch
        {
            "/" or "/index.html" => (200, "text/html; charset=utf-8", StatusPage.Html),
            "/status.json" => (200, "application/json; charset=utf-8", board.ToJson()),
            _ => (404, "text/plain; charset=utf-8", "Not found"),
        };
        var bytes = Encoding.UTF8.GetBytes(body);
        response.StatusCode = status;
        response.ContentType = type;
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    public async ValueTask DisposeAsync()
    {
        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        if (_loop is not null)
        {
            await _loop.ConfigureAwait(false);
        }

        _listener.Close();
    }
}

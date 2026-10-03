using System.Threading.Channels;

namespace XPConnect.Host.Transport;

/// <summary>Accepts clients from several listeners at once (the default MSFS pipe and XPConnect's TCP port).</summary>
public sealed class MultiListener(IReadOnlyList<IListener> listeners, Action<string> log) : IListener
{
    private readonly Channel<Stream> _accepted = Channel.CreateUnbounded<Stream>();
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _lock = new();
    private List<Task>? _pumps;

    public Endpoint Endpoint => listeners[0].Endpoint;

    public IReadOnlyList<IListener> Listeners => listeners;

    public string Description => string.Join(" + ", listeners.Select(l => l.Endpoint));

    public async Task<Stream> AcceptAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            _pumps ??= [.. listeners.Select(Pump)];
        }

        return await _accepted.Reader.ReadAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_pumps is not null)
        {
            await Task.WhenAll(_pumps).ConfigureAwait(false);
        }

        foreach (var listener in listeners)
        {
            await listener.DisposeAsync().ConfigureAwait(false);
        }

        while (_accepted.Reader.TryRead(out var orphan))
        {
            await orphan.DisposeAsync().ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    private async Task Pump(IListener listener)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await listener.AcceptAsync(_stop.Token).ConfigureAwait(false);
                await _accepted.Writer.WriteAsync(client, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Sockets.SocketException)
        {
            log($"listen: stopped listening on {listener.Endpoint}: {ex.Message}");
        }
    }
}

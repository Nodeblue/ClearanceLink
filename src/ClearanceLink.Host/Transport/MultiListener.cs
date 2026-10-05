using System.Threading.Channels;

namespace ClearanceLink.Host.Transport;

/// <summary>
/// Accepts clients from several listeners at once (the default MSFS pipe and ClearanceLink's TCP port). An accept
/// error never stops a transport: it is logged and retried after a delay that grows from 1 s to 5 s, until disposed.
/// </summary>
public sealed class MultiListener(IReadOnlyList<IListener> listeners, Action<string> log, TimeSpan? firstRetryDelay = null) : IListener
{
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);

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
        var first = firstRetryDelay ?? TimeSpan.FromSeconds(1);
        var delay = first;
        string? lastError = null;
        var failures = 0;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptAsync(_stop.Token).ConfigureAwait(false);
                delay = first;
                if (failures > 0)
                {
                    log($"listen: accepting again on {listener.Endpoint} after {failures} failed accept(s)");
                    failures = 0;
                    lastError = null;
                }

                await _accepted.Writer.WriteAsync(client, _stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A transient error (pipe busy, socket reset during accept) must not stop this transport for the session.
                failures++;
                var error = $"{ex.GetType().Name}: {ex.Message}";
                if (error != lastError)
                {
                    // Logged when the error changes, so a listener that keeps failing does not fill the log every 5 s.
                    log($"listen: accept failed on {listener.Endpoint}: {error}; retrying every {delay.TotalSeconds:0.#}-{Math.Max(MaxRetryDelay.TotalSeconds, first.TotalSeconds):0.#}s");
                    lastError = error;
                }

                try
                {
                    await Task.Delay(delay, _stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, Math.Max(MaxRetryDelay.Ticks, first.Ticks)));
            }
        }
    }
}

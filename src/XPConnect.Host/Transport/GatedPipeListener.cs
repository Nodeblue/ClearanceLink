namespace XPConnect.Host.Transport;

/// <summary>
/// The default MSFS pipe, opened only while MSFS is closed (<see cref="MsfsGate"/>). When MSFS starts, the waiting
/// pipe instance and every client connected through the pipe are closed so MSFS can take the pipe back.
/// </summary>
public sealed class GatedPipeListener : IListener
{
    private readonly IListener _pipe;
    private readonly MsfsGate _gate;
    private readonly Lock _lock = new();
    private readonly List<Stream> _clients = [];

    public GatedPipeListener(Endpoint endpoint, MsfsGate gate)
        : this(Transports.Listen(endpoint), gate)
    {
    }

    public GatedPipeListener(IListener pipe, MsfsGate gate)
    {
        _pipe = pipe;
        _gate = gate;
        _gate.Changed += OnMsfsChanged;
    }

    public Endpoint Endpoint => _pipe.Endpoint;

    public async Task<Stream> AcceptAsync(CancellationToken ct)
    {
        while (true)
        {
            await _gate.WaitUntilClosedAsync(ct).ConfigureAwait(false);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _gate.WhileClosed);
            try
            {
                var client = await _pipe.AcceptAsync(linked.Token).ConfigureAwait(false);
                lock (_lock)
                {
                    _clients.RemoveAll(c => !c.CanRead);
                    _clients.Add(client);
                }

                return client;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // MSFS started while waiting: wait for it to close again.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _gate.Changed -= OnMsfsChanged;
        await _pipe.DisposeAsync().ConfigureAwait(false);
    }

    private void OnMsfsChanged(bool running)
    {
        if (!running)
        {
            return;
        }

        List<Stream> clients;
        lock (_lock)
        {
            clients = [.. _clients];
            _clients.Clear();
        }

        clients.ForEach(c => c.Dispose());
    }
}

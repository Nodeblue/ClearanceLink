using System.Diagnostics;

namespace XPConnect.Host.Transport;

/// <summary>
/// Watches for the real Microsoft Flight Simulator. MSFS owns the default SimConnect pipe, so XPConnect only
/// listens there while MSFS is closed, and steps aside (drops pipe clients, stops the stand-in) when it starts.
/// </summary>
public sealed class MsfsGate(Func<bool> detect, Action<string> log, TimeSpan? poll = null) : IDisposable
{
    private static readonly string[] SimProcessNames = [StubLauncher.ProcessName, "FlightSimulator"];

    private readonly Lock _gate = new();
    private readonly TimeSpan _poll = poll ?? TimeSpan.FromSeconds(3);
    private bool? _running;
    private TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource _whileClosed = new();

    /// <summary>Raised with true when MSFS starts and false when it closes (and once on the first check).</summary>
    public event Action<bool>? Changed;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _running ?? false;
            }
        }
    }

    /// <summary>Cancelled the moment MSFS is seen starting; a fresh token after each close.</summary>
    public CancellationToken WhileClosed
    {
        get
        {
            lock (_gate)
            {
                return _whileClosed.Token;
            }
        }
    }

    public void Check()
    {
        var running = detect();
        lock (_gate)
        {
            if (_running == running)
            {
                return;
            }

            _running = running;
            if (running)
            {
                _closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _whileClosed.Cancel();
            }
            else
            {
                _whileClosed.Dispose();
                _whileClosed = new CancellationTokenSource();
                _closed.TrySetResult();
            }
        }

        log(running
            ? "msfs: Microsoft Flight Simulator is running; BeyondATC can't reach X-Plane on the default connection until it is closed"
            : "msfs: not running; default connection open for BeyondATC");
        Changed?.Invoke(running);
    }

    public Task WaitUntilClosedAsync(CancellationToken ct)
    {
        if (!Checked)
        {
            Check();
        }

        lock (_gate)
        {
            return _closed.Task.WaitAsync(ct);
        }
    }

    private bool Checked
    {
        get
        {
            lock (_gate)
            {
                return _running is not null;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _whileClosed.Dispose();
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Check();
                await Task.Delay(_poll, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>True when a FlightSimulator2024 or FlightSimulator process is running that is not XPConnect's stand-in.</summary>
    public static bool RealMsfsRunning()
    {
        foreach (var name in SimProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (!StubLauncher.IsStandIn(process))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}

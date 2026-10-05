namespace ClearanceLink.Host.Pushback;

/// <summary>Whether the Better Pushback plugin is loaded in X-Plane.</summary>
public enum PushbackAddOn
{
    /// <summary>X-Plane is not connected, or has not been asked yet.</summary>
    Unknown,
    Installed,
    NotInstalled,
}

/// <summary>
/// Finds out whether Better Pushback is loaded in X-Plane, by asking the Web API for one of its commands: when
/// X-Plane connects, then every <see cref="Recheck"/> (the plugin can be enabled, disabled or reloaded while X-Plane
/// runs). Pushback for BeyondATC (its GSX requests) is only passed on when <see cref="Available"/> is true; without
/// Better Pushback, nothing is sent to X-Plane for it.
/// </summary>
public sealed class BetterPushback(
    Func<bool> connected,
    Func<string, CancellationToken, Task<bool>> hasCommand,
    Action<string> log,
    TimeProvider? time = null)
{
    /// <summary>The command looked for; every Better Pushback version since 1.0 has it.</summary>
    public const string Probe = "BetterPushback/start";

    public static readonly TimeSpan Recheck = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(2);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private volatile PushbackAddOn _state = PushbackAddOn.Unknown;
    private long _lastCheck;

    public PushbackAddOn State => _state;

    /// <summary>True only when Better Pushback is loaded; the gate for sending any pushback command to X-Plane.</summary>
    public bool Available => _state == PushbackAddOn.Installed;

    /// <summary>
    /// When to ask X-Plane again. Pure so it can be tested: at once after (re)connecting, then every
    /// <see cref="Recheck"/>; never while X-Plane is not connected.
    /// </summary>
    public static bool Due(bool connected, PushbackAddOn state, TimeSpan sinceLastCheck) =>
        connected && (state == PushbackAddOn.Unknown || sinceLastCheck >= Recheck);

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await CheckOnceAsync(ct).ConfigureAwait(false);
                await Task.Delay(Tick, _time, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Closing.
        }
    }

    /// <summary>One round of <see cref="RunAsync"/>; public for the tests.</summary>
    public async Task CheckOnceAsync(CancellationToken ct = default)
    {
        if (!connected())
        {
            Set(PushbackAddOn.Unknown);
            return;
        }

        if (!Due(true, _state, _time.GetElapsedTime(_lastCheck)))
        {
            return;
        }

        _lastCheck = _time.GetTimestamp();
        try
        {
            Set(await hasCommand(Probe, ct).ConfigureAwait(false) ? PushbackAddOn.Installed : PushbackAddOn.NotInstalled);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // X-Plane went away mid-question; the next round asks again once it is back.
            log($"pushback check failed: {ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}");
            Set(PushbackAddOn.Unknown);
        }
    }

    private void Set(PushbackAddOn state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        switch (state)
        {
            case PushbackAddOn.Installed:
                log($"pushback Better Pushback found ({Probe}): BeyondATC pushback can be passed on");
                break;
            case PushbackAddOn.NotInstalled:
                log($"pushback Better Pushback NOT FOUND ({Probe}): BeyondATC pushback (GSX) requests are not passed on to X-Plane");
                break;
        }
    }
}

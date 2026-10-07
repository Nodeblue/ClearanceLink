using ClearanceLink.Host.Support;

namespace ClearanceLink.Host;

/// <summary>
/// Connection automatic: BeyondATC is pointed at ClearanceLink's TCP port only while ClearanceLink runs and MSFS is
/// closed, so MSFS's default SimConnect pipe stays free and other MSFS add-ons (SPAD.neXt, SayIntentions) never see
/// a half-there MSFS (ADR-0012). The two SimConnect.cfg files are written when serve starts or MSFS closes, and
/// removed when MSFS starts or ClearanceLink stops. A guard process (<c>batc-guard</c>) removes them if ClearanceLink
/// ends without doing so (a crash, or killed).
/// </summary>
public sealed class BatcLink(BatcRedirect redirect, Action<string> log, Action? startGuard = null)
{
    private readonly Lock _gate = new();
    private bool _guardStarted;
    private Timer? _keepOn;
    private Func<bool> _stillAlwaysOn = () => true;
    private bool _released;

    /// <summary>Why the files could not be written or removed the last time; null when that worked.</summary>
    public string? Problem { get; private set; }

    public BatcRedirect Redirect => redirect;

    /// <summary>
    /// True when serve should use the redirect: BeyondATC's folder is known and both its folders can be written.
    /// Otherwise serve falls back to answering on MSFS's default pipe.
    /// </summary>
    public static bool Usable(BatcRedirect redirect) => redirect.CanWrite();

    /// <summary>Follows MSFS (<see cref="Transport.MsfsGate.Changed"/>): files off while it runs, on while it is closed.</summary>
    public void Follow(bool msfsRunning)
    {
        lock (_gate)
        {
            if (msfsRunning)
            {
                Apply(redirect.SwitchOff, "batc: MSFS is running; BeyondATC pointed back at MSFS");
                return;
            }

            if (!_guardStarted && startGuard is not null)
            {
                _guardStarted = true;
                startGuard();
            }

            Apply(redirect.SwitchOn, "batc: BeyondATC pointed at ClearanceLink until it closes");
        }
    }

    /// <summary>
    /// Connection always on: puts BeyondATC's two files back when they are missing or no longer point at ClearanceLink.
    /// A BeyondATC update can replace its folder and take them with it (issue #35); serve calls this when it starts
    /// and every few seconds after (<see cref="KeepOnEvery"/>).
    /// </summary>
    public void KeepOn()
    {
        lock (_gate)
        {
            // A timer tick that ran late, after serve stopped, or always on undone from the command line meanwhile.
            if (_released || !_stillAlwaysOn())
            {
                return;
            }

            Apply(redirect.SwitchOn, "batc: always on; BeyondATC's config files were missing, written again", logOnce: true);
        }
    }

    /// <summary>
    /// Calls <see cref="KeepOn"/> now and then every <paramref name="period"/> until <see cref="Release"/>, while
    /// <paramref name="stillAlwaysOn"/> says always on is still chosen (the command line can change it).
    /// </summary>
    public void KeepOnEvery(TimeSpan period, Func<bool> stillAlwaysOn)
    {
        lock (_gate)
        {
            _stillAlwaysOn = stillAlwaysOn;
            _keepOn ??= new Timer(_ => KeepOn(), null, TimeSpan.Zero, period);
        }
    }

    /// <summary>Serve is stopping: BeyondATC goes back to MSFS, unless the user has just chosen always on.</summary>
    public void Release(ConnectionMode now)
    {
        lock (_gate)
        {
            _released = true;
            _keepOn?.Dispose();
            _keepOn = null;
            if (now == ConnectionMode.Automatic)
            {
                Apply(redirect.SwitchOff, "batc: ClearanceLink closing; BeyondATC pointed back at MSFS");
            }
        }
    }

    /// <summary>
    /// What the guard does once ClearanceLink has gone: remove the files, unless always on was chosen meanwhile or
    /// another ClearanceLink is already serving on the port (it has written them again).
    /// </summary>
    public static bool GuardShouldRelease(ConnectionMode now, bool portInUse) => now == ConnectionMode.Automatic && !portInUse;

    private void Apply(Func<IReadOnlyList<string>> change, string line, bool logOnce = false)
    {
        try
        {
            var done = change();
            Problem = null;
            if (done.Any(d => !d.StartsWith("already set", StringComparison.Ordinal) && !d.StartsWith("left alone", StringComparison.Ordinal)))
            {
                log($"{line} ({string.Join("; ", done)})");
            }
        }
        catch (RedirectFailedException ex)
        {
            // Always on retries every few seconds: the same problem is logged once.
            if (!logOnce || Problem != ex.Message)
            {
                log($"batc: {ex.Message}");
            }

            Problem = ex.Message;
        }
    }
}

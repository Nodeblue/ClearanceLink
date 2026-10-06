using ClearanceLink.Airports;
using ClearanceLink.Server;

namespace ClearanceLink.Host.Pushback;

/// <summary>What the PUSH page shows.</summary>
public sealed record PushView(
    PushClearance? Clearance = null,
    PushChoice? Choice = null,
    PushPlan? Preview = null,
    PushStage? Stage = null,
    string? Problem = null,
    bool Stalled = false,
    bool BrakeSet = false,
    double Pushed = 0,
    bool XPlane = false,
    bool? BetterPushback = null,
    bool Automatic = false)
{
    /// <summary>What START PUSH would do: the pilot's choice, else BATC's clearance.</summary>
    public PushChoice? Selected => Choice ?? (Clearance is null ? null : PushChoice.Face);

    public bool Running => Stage is not (null or PushStage.Done or PushStage.Failed);

    /// <summary>START PUSH can be pressed: a clearance or choice, and Better Pushback not known to be missing.</summary>
    public bool CanStart => Selected is not null && BetterPushback is not false;
}

/// <summary>
/// The PUSH page's pushback: picks up BeyondATC's pushback approvals from its log, plans the push from the aircraft's
/// position and the airport's taxiways, and when the pilot presses START PUSH drives Better Pushback through
/// <see cref="PushDriver"/>. Nothing moves until the pilot presses the key (ADR-0013).
/// </summary>
public sealed class Pushbacks(
    IXPlaneData xplane,
    Func<IReadOnlyList<string>> airports,
    Func<string, Airport?> loadAirport,
    BatcLogFollower? batcLog,
    Action<string> log,
    TimeProvider? time = null,
    TimeSpan? detectAfter = null)
{
    private const string Latitude = "sim/flightmodel/position/latitude";
    private const string Longitude = "sim/flightmodel/position/longitude";
    private const string Heading = "sim/flightmodel/position/psi";
    private const string GroundSpeed = "sim/flightmodel/position/groundspeed";
    private const string ParkingBrake = "sim/cockpit2/controls/parking_brake_ratio";
    private const string Started = "bp/started";
    private const string Connected = "bp/connected";

    /// <summary>Gear positions along the aircraft (metres from the CG), for its wheelbase; Better Pushback reads them too.</summary>
    private static readonly string[] TireZ = [.. Enumerable.Range(0, 10).Select(i => $"sim/flightmodel/parts/tire_z_no_deflection[{i}]")];

    /// <summary>How long a clearance stays on the page; BATC repeats it if the pilot asks again.</summary>
    private static readonly TimeSpan ClearanceKept = TimeSpan.FromMinutes(20);

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How long X-Plane must be sending the aircraft's position without Better Pushback's bp/started before Better
    /// Pushback counts as not installed (a dataref's first value can take a moment after subscribing).
    /// </summary>
    public static readonly TimeSpan DetectAfter = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (Airport? Airport, IReadOnlyList<TaxiwaySegment> Taxiways)> _taxiways = new(StringComparer.OrdinalIgnoreCase);
    private PushView _view = new();
    private PushDriver? _driver;
    private bool _startRequested;
    private DateTimeOffset _lastLogPoll = DateTimeOffset.MinValue;
    private TimeSpan? _positionSince;
    private bool _autoPending;
    private bool _replan;

    public PushView View
    {
        get
        {
            lock (_gate)
            {
                return _view;
            }
        }
    }

    /// <summary>
    /// Start as soon as BeyondATC's approval arrives (the way BeyondATC drives GSX in MSFS), when Better Pushback is
    /// there and no push is running. Off: wait for START PUSH.
    /// </summary>
    public bool Automatic
    {
        get
        {
            lock (_gate)
            {
                return _view.Automatic;
            }
        }

        set
        {
            lock (_gate)
            {
                _view = _view with { Automatic = value };
            }
        }
    }

    /// <summary>
    /// TAIL LEFT, TAIL RIGHT or NO TURN (null: AS ATC SAID). Works until the tug starts moving; a push already called
    /// is planned again. Returns the scratchpad line, empty when there is nothing to say.
    /// </summary>
    public string Choose(PushChoice? choice)
    {
        lock (_gate)
        {
            if (_view.Running && _driver is { Stage: not (PushStage.CallingTug or PushStage.WaitingForBrake) })
            {
                return "PUSH IN PROGRESS";
            }

            if (choice is null && _view.Clearance is null)
            {
                return "NO ATC CLEARANCE";
            }

            _view = _view with { Choice = choice };
            _replan = _view.Running;
            return "";
        }
    }

    /// <summary>START PUSH: returns the scratchpad line.</summary>
    public string Start()
    {
        lock (_gate)
        {
            if (_view.Running)
            {
                return "PUSH IN PROGRESS";
            }

            if (!xplane.IsConnected)
            {
                return "START X-PLANE 12";
            }

            if (_view.BetterPushback is false)
            {
                return "BETTER PUSHBACK MISSING";
            }

            if (_view.Selected is null)
            {
                return "WAIT FOR CLEARANCE";
            }

            _startRequested = true;
            return "CALLING TUG";
        }
    }

    /// <summary>STOP: ends the push where it is (Better Pushback still disconnects the tug).</summary>
    public string Stop()
    {
        lock (_gate)
        {
            if (_driver is null || _driver.Finished)
            {
                return "NO PUSH RUNNING";
            }

            _driver.RequestStop();
            return "STOPPING";
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        foreach (var dataref in new[] { Latitude, Longitude, Heading, GroundSpeed, ParkingBrake, Started, Connected }.Concat(TireZ))
        {
            xplane.Watch(dataref);
        }

        var started = _time.GetTimestamp();
        var previewAt = TimeSpan.FromSeconds(-1);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(Tick, _time, ct).ConfigureAwait(false);
                try
                {
                    previewAt = await TickAsync(_time.GetElapsedTime(started), previewAt, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A bug here must not end pushbacks for the session, nor the app.
                    log($"pushback: step failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Closing.
        }
    }

    private async Task<TimeSpan> TickAsync(TimeSpan now, TimeSpan previewAt, CancellationToken ct)
    {
        PollClearance();
        var inputs = Read();
        var found = Detect(inputs is not null, now);
        lock (_gate)
        {
            if (found != _view.BetterPushback && found is not null)
            {
                log($"pushback: Better Pushback {(found.Value ? "found" : "not found (no bp/started dataref); PUSH sends nothing")}");
            }

            _view = _view with { XPlane = xplane.IsConnected, BrakeSet = inputs?.ParkingBrake >= 0.9, BetterPushback = found };

            // ON CLEARANCE: BeyondATC's approval calls the tug, as it calls GSX in MSFS, once Better Pushback is known
            // to be there. Without it the page says BETTER PUSHBACK MISSING and nothing is sent.
            if (_autoPending && (!_view.Automatic || _view.Clearance is null || _view.Running || found is false))
            {
                _autoPending = false;
            }
            else if (_autoPending && found is true && inputs is not null)
            {
                _autoPending = false;
                _startRequested = true;
                log("pushback: automatic start on BATC's clearance");
            }
        }

        if (inputs is not { } x)
        {
            return previewAt;
        }

        if (TakeStart(x) is { } driver)
        {
            log($"pushback: START {Describe(driver.Plan)}");
        }

        Replan(x);

        if (_driver is { Finished: false } running)
        {
            await StepAsync(running, x, now.TotalSeconds, ct).ConfigureAwait(false);
        }
        else if (now - previewAt >= TimeSpan.FromSeconds(1))
        {
            previewAt = now;
            var preview = PlanFor(x.Pose);
            lock (_gate)
            {
                _view = _view with { Preview = preview };
            }
        }

        return previewAt;
    }

    private async Task StepAsync(PushDriver driver, PushInputs x, double seconds, CancellationToken ct)
    {
        var before = driver.Stage;
        IReadOnlyList<string> commands;
        lock (_gate)
        {
            commands = driver.Step(x, seconds);
        }

        foreach (var command in commands)
        {
            try
            {
                await xplane.CommandAsync(command, ct).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(120), _time, ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                // X-Plane has no such command: Better Pushback isn't installed (or is too old for manual push).
                log($"pushback: command {command} failed: {ex.Message}");
                lock (_gate)
                {
                    driver.Abandon("BETTER PUSHBACK MISSING");
                }

                break;
            }
            catch (HttpRequestException ex)
            {
                log($"pushback: command {command} not sent: {ex.Message}");
            }
        }

        if (driver.Stage != before)
        {
            log($"pushback: {before} -> {driver.Stage} pushed={driver.Pushed:0.0}m hdg={x.Heading:0.0}{(driver.Problem is { } p ? $" problem={p}" : "")}");
        }

        lock (_gate)
        {
            _view = _view with { Stage = driver.Stage, Problem = driver.Problem, Stalled = driver.Stalled, Pushed = driver.Pushed };
            if (driver.Stage == PushStage.Done)
            {
                // Used: the next push needs a new clearance or choice.
                _view = _view with { Clearance = null, Choice = null };
            }
        }
    }

    /// <summary>
    /// Better Pushback is installed when X-Plane has its bp/started dataref. The Web API looks datarefs up again on
    /// every connection, so a restart of X-Plane after installing it is picked up.
    /// </summary>
    private bool? Detect(bool position, TimeSpan now)
    {
        if (!position)
        {
            _positionSince = null;
            return null;
        }

        _positionSince ??= now;
        if (xplane.TryGetNumber(Started, out _))
        {
            return true;
        }

        return now - _positionSince.Value >= (detectAfter ?? DetectAfter) ? false : null;
    }

    private PushDriver? TakeStart(PushInputs x)
    {
        lock (_gate)
        {
            if (!_startRequested)
            {
                return null;
            }

            _startRequested = false;
            if (_view.Selected is not { } choice || _view.BetterPushback is false)
            {
                return null;
            }

            var plan = PushPlanner.Plan(x.Pose, choice, _view.Clearance?.FaceBearing, TaxiwaysAt(x.Pose), Wheelbase());
            _driver = new PushDriver(plan);
            _view = _view with { Preview = plan, Stage = PushStage.CallingTug, Problem = null, Stalled = false, Pushed = 0 };
            return _driver;
        }
    }

    /// <summary>The pilot changed the turn while the tug was on its way: plan again from where the aircraft is.</summary>
    private void Replan(PushInputs x)
    {
        lock (_gate)
        {
            if (!_replan || _driver is null || _view.Selected is not { } choice)
            {
                return;
            }

            _replan = false;
            var plan = PushPlanner.Plan(x.Pose, choice, _view.Clearance?.FaceBearing, TaxiwaysAt(x.Pose), Wheelbase());
            if (_driver.Replan(plan))
            {
                _view = _view with { Preview = plan };
                log($"pushback: replanned {Describe(plan)}");
            }
        }
    }

    private PushPlan? PlanFor(Pose pose)
    {
        PushView view;
        lock (_gate)
        {
            view = _view;
        }

        return view.Selected is { } choice ? PushPlanner.Plan(pose, choice, view.Clearance?.FaceBearing, TaxiwaysAt(pose), Wheelbase()) : null;
    }

    private void PollClearance()
    {
        var now = _time.GetUtcNow();
        if (batcLog is null || now - _lastLogPoll < TimeSpan.FromSeconds(1))
        {
            return;
        }

        _lastLogPoll = now;
        var found = batcLog.Poll();
        lock (_gate)
        {
            if (found.Count > 0 && !_view.Running)
            {
                var clearance = found[^1];
                log($"pushback: BATC cleared pushback{(clearance.FaceWord is { } w ? $", face {w}" : "")}: {clearance.Text}");
                _view = _view with { Clearance = clearance, Choice = null, Stage = _view.Stage is PushStage.Done or PushStage.Failed ? null : _view.Stage };
                _autoPending = _view.Automatic;
            }
            else if (_view.Clearance is { } old && now - old.At > ClearanceKept && !_view.Running)
            {
                _view = _view with { Clearance = null };
            }
        }
    }

    /// <summary>Nose gear to main gear, metres; null until X-Plane has sent the gear positions.</summary>
    private double? Wheelbase()
    {
        var z = TireZ.Select(d => xplane.TryGetNumber(d, out var v) ? v : 0).ToList();
        var wheelbase = z.Max() - z.Min();
        return wheelbase > 1 ? wheelbase : null;
    }

    private PushInputs? Read()
    {
        if (!xplane.IsConnected
            || !xplane.TryGetNumber(Latitude, out var lat)
            || !xplane.TryGetNumber(Longitude, out var lon)
            || !xplane.TryGetNumber(Heading, out var hdg)
            || !xplane.TryGetNumber(GroundSpeed, out var gs))
        {
            return null;
        }

        xplane.TryGetNumber(ParkingBrake, out var brake);
        xplane.TryGetNumber(Started, out var started);
        xplane.TryGetNumber(Connected, out var connected);
        return new PushInputs(lat, lon, hdg, gs, brake, started >= 0.5, connected >= 0.5);
    }

    /// <summary>Taxiways of the airport BATC asked for that the aircraft is at (within 10 km of its reference point).</summary>
    private IReadOnlyList<TaxiwaySegment> TaxiwaysAt(Pose pose)
    {
        foreach (var icao in airports())
        {
            if (!_taxiways.TryGetValue(icao, out var known))
            {
                Airport? airport = null;
                try
                {
                    airport = loadAirport(icao);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException)
                {
                    log($"pushback: {icao} taxiways not read: {ex.Message}");
                }

                known = (airport, airport is null ? [] : PushPlanner.Taxiways(airport));
                _taxiways[icao] = known;
            }

            if (known.Airport is { } a && Geo.DistanceMeters(a.ReferencePoint.Latitude, a.ReferencePoint.Longitude, pose.Latitude, pose.Longitude) < 10_000)
            {
                return known.Taxiways;
            }
        }

        return [];
    }

    public static string Describe(PushPlan plan) => !plan.Turns
        ? $"straight back {plan.StraightMeters:0}m"
        : $"back {plan.StraightMeters:0}m then tail {(plan.TailLeft ? "left" : "right")} {Math.Abs(plan.TurnDegrees):0} deg to {plan.FinalHeading:000}{(plan.Taxiway is { } t ? $" on {t}" : "")}";
}

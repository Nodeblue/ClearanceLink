using ClearanceLink.Airports;

namespace ClearanceLink.Host.Pushback;

/// <summary>What the driver reads from X-Plane on every step.</summary>
public readonly record struct PushInputs(
    double Latitude,
    double Longitude,
    double Heading,
    double GroundSpeed,
    double ParkingBrake,
    bool Started,
    bool Connected)
{
    public Pose Pose => new(Latitude, Longitude, Heading);
}

public enum PushStage
{
    CallingTug,
    WaitingForBrake,
    PushingBack,
    Turning,
    Straightening,
    Stopping,
    Disconnecting,
    Done,
    Failed,
}

/// <summary>
/// Drives Better Pushback's manual push (1.14, olivierbutler fork) to carry out a <see cref="PushPlan"/>: calls the
/// tug, waits while the pilot releases the parking brake, pushes straight, steers the tail round, straightens,
/// pauses, then stops and, once the pilot sets the parking brake, disconnects the tug and acknowledges the wing
/// walker. Every action is a Better Pushback command (docs/decisions/ADR-0013). A pure state machine: each step takes
/// X-Plane's values and the time, and returns the commands to send, so it is tested without X-Plane.
/// </summary>
public sealed class PushDriver(PushPlan plan)
{
    private PushPlan _plan = plan;

    public const string StartCommand = "BetterPushback/manual_push_start_no_yoke";
    public const string LeftCommand = "BetterPushback/manual_push_left";
    public const string RightCommand = "BetterPushback/manual_push_right";
    public const string StopCommand = "BetterPushback/stop";
    public const string DisconnectCommand = "BetterPushback/disconnect";
    public const string AcknowledgeCommand = "BetterPushback/acknowledge_clear";

    /// <summary>Steering while turning, in Better Pushback's 10% steps of the nose wheel's limit (5 = 50%).</summary>
    public const int TurnSteps = 5;

    /// <summary>
    /// Seconds of turn to leave when centring the steering: the Citation X kept turning about 6 degrees at 4 deg/s
    /// after centring (Ben's PC 2026-10-05).
    /// </summary>
    public const double TurnLeadSeconds = 1.5;

    /// <summary>How far to push after the turn so the nose wheel lines up before stopping.</summary>
    public const double StraightenMeters = 2;

    /// <summary>Stops the push if the aircraft gets this far from where it started.</summary>
    public const double MaxMeters = 120;

    /// <summary>A position change this big between two steps is a flight reload or reposition, not a push.</summary>
    public const double JumpMeters = 30;

    private const double StartTimeoutSeconds = 20;
    private const double RetrySeconds = 5;
    private const double MovingSpeed = 0.2;
    private const double StoppedSpeed = 0.05;
    private const double StalledSeconds = 40;

    /// <summary>Better Pushback's pause took about 5 s to bring the Citation X to a stop (Ben's PC 2026-10-05).</summary>
    private const double PauseSeconds = 8;

    private Pose? _origin;
    private Pose? _last;
    private double _turnStartHeading;
    private double _straightenFrom;
    private double _stageSince;
    private double _lastRetry = double.NegativeInfinity;
    private (double Time, double Heading)? _rateSample;
    private double _rate;
    private bool _wasStarted;
    private bool _stopRequested;
    private double? _brakeOffSince;

    public PushPlan Plan => _plan;

    /// <summary>A new plan from the pilot while the tug is still coming or waiting for the brake; false once moving.</summary>
    public bool Replan(PushPlan newPlan)
    {
        if (Stage is not (PushStage.CallingTug or PushStage.WaitingForBrake))
        {
            return false;
        }

        _plan = newPlan;
        return true;
    }

    public PushStage Stage { get; private set; } = PushStage.CallingTug;

    /// <summary>Why the push ended early, for the page and the log.</summary>
    public string? Problem { get; private set; }

    /// <summary>How far the aircraft has been pushed back along its starting line, metres.</summary>
    public double Pushed { get; private set; }

    /// <summary>
    /// The tug is on and the brake is off, but nothing moves. Better Pushback 1.14 can't move some aircraft at all
    /// (the Aerobask Phenom 300 on Ben's PC, 2026-10-05, also with its own planned push).
    /// </summary>
    public bool Stalled { get; private set; }

    public bool Finished => Stage is PushStage.Done or PushStage.Failed;

    /// <summary>The pilot pressed STOP: stop now, then disconnect as usual.</summary>
    public void RequestStop() => _stopRequested = true;

    /// <summary>Gives up without sending anything more (Better Pushback is missing).</summary>
    public void Abandon(string problem) => Fail(problem, []);

    /// <summary>A command didn't reach Better Pushback. Before the tug moves the push is dropped; after, it is stopped.</summary>
    public void CommandFailed()
    {
        if (Stage is PushStage.PushingBack or PushStage.Turning or PushStage.Straightening)
        {
            Problem = "COMMAND FAILED: STOPPED";
            _stopRequested = true;
        }
        else if (Stage is PushStage.CallingTug && !_wasStarted)
        {
            Fail("COMMAND FAILED", []);
        }
    }

    /// <summary>One step: returns the commands to send now, in order.</summary>
    public IReadOnlyList<string> Step(PushInputs x, double now)
    {
        var commands = new List<string>();
        if (Finished)
        {
            return commands;
        }

        if (_origin is null)
        {
            _origin = x.Pose;
            _last = x.Pose;
            _stageSince = now;
            commands.Add(StartCommand);
            return commands;
        }

        if (Geo.DistanceMeters(_last!.Value.Latitude, _last.Value.Longitude, x.Latitude, x.Longitude) > JumpMeters)
        {
            return Fail("FLIGHT RELOADED", commands);
        }

        _last = x.Pose;
        Pushed = AlongTail(_origin.Value, x.Pose);
        _wasStarted |= x.Started;
        if (_wasStarted && !x.Started)
        {
            // Better Pushback finished (or the pilot ended it in its own window).
            Stage = Stage == PushStage.Disconnecting ? PushStage.Done : PushStage.Failed;
            Problem = Stage == PushStage.Failed ? "ENDED IN BETTER PUSHBACK" : null;
            return commands;
        }

        var far = Geo.DistanceMeters(_origin.Value.Latitude, _origin.Value.Longitude, x.Latitude, x.Longitude) > MaxMeters;
        if ((_stopRequested || far) && Stage is < PushStage.Stopping)
        {
            Problem = far ? "STOPPED: TOO FAR" : Problem;
            if (Stage is PushStage.PushingBack or PushStage.Turning or PushStage.Straightening)
            {
                commands.Add(StartCommand); // pauses a running manual push
                Move(PushStage.Stopping, now);
                return commands;
            }

            commands.Add(StopCommand);
            Move(_wasStarted ? PushStage.Disconnecting : PushStage.Done, now);
            return commands;
        }

        switch (Stage)
        {
            case PushStage.CallingTug:
                if (x.Connected)
                {
                    Move(PushStage.WaitingForBrake, now);
                }
                else if (!_wasStarted && now - _stageSince > StartTimeoutSeconds)
                {
                    return Fail("BETTER PUSHBACK DIDN'T START", commands);
                }

                break;
            case PushStage.WaitingForBrake:
                if (x.GroundSpeed > MovingSpeed)
                {
                    Move(PushStage.PushingBack, now);
                    Stalled = false;
                }
                else if (x.ParkingBrake >= 0.1)
                {
                    _brakeOffSince = null;
                }
                else
                {
                    _brakeOffSince ??= now;
                    Stalled = now - _brakeOffSince.Value > StalledSeconds;
                }

                break;
            case PushStage.PushingBack:
                if (Pushed >= _plan.StraightMeters)
                {
                    if (_plan.Turns)
                    {
                        // manual_push_right swings the tail right, so the heading falls (Ben's PC 2026-10-05).
                        commands.AddRange(Enumerable.Repeat(_plan.TailLeft ? LeftCommand : RightCommand, TurnSteps));
                        _turnStartHeading = x.Heading;
                        _rateSample = (now, x.Heading);
                        _rate = 0;
                        Move(PushStage.Turning, now);
                    }
                    else
                    {
                        commands.Add(StartCommand);
                        Move(PushStage.Stopping, now);
                    }
                }

                break;
            case PushStage.Turning:
                if (_rateSample is { } sample && now - sample.Time >= 1)
                {
                    _rate = Math.Abs(PushPlanner.Delta(sample.Heading, x.Heading)) / (now - sample.Time);
                    _rateSample = (now, x.Heading);
                }

                var turned = PushPlanner.Delta(_turnStartHeading, x.Heading) * Math.Sign(_plan.TurnDegrees);
                var left = Math.Abs(_plan.TurnDegrees) - turned;
                if (left <= Math.Max(2, _rate * TurnLeadSeconds))
                {
                    commands.AddRange(Enumerable.Repeat(_plan.TailLeft ? RightCommand : LeftCommand, TurnSteps));
                    _straightenFrom = Pushed;
                    Move(PushStage.Straightening, now);
                }

                break;
            case PushStage.Straightening:
                if (now - _stageSince >= 2 && Math.Abs(Pushed - _straightenFrom) >= StraightenMeters)
                {
                    commands.Add(StartCommand);
                    Move(PushStage.Stopping, now);
                }

                break;
            case PushStage.Stopping:
                if (x.GroundSpeed < StoppedSpeed && now - _stageSince >= 1)
                {
                    commands.Add(StopCommand);
                    Move(PushStage.Disconnecting, now);
                }
                else if (now - _stageSince >= PauseSeconds && x.GroundSpeed > MovingSpeed && now - _lastRetry >= RetrySeconds)
                {
                    // Still pushing: the pause was lost (it toggles, so it isn't sent again). Stop ends the push outright.
                    commands.Add(StopCommand);
                    _lastRetry = now;
                }

                break;
            case PushStage.Disconnecting:
                // Better Pushback lowers the nose wheel once the parking brake is set, then waits for "disconnect"
                // and, after the wing walker's clear signal, for an acknowledgement. Each is ignored until its
                // moment comes, so both are repeated until the tug has gone.
                if (x.Started && x.GroundSpeed > MovingSpeed && now - _lastRetry >= RetrySeconds)
                {
                    // Moving again after the stop: the stop was lost.
                    commands.Add(StopCommand);
                    _lastRetry = now;
                }
                else if (x.ParkingBrake >= 0.9 && now - _lastRetry >= RetrySeconds)
                {
                    commands.Add(DisconnectCommand);
                    commands.Add(AcknowledgeCommand);
                    _lastRetry = now;
                }

                break;
        }

        return commands;
    }

    /// <summary>Metres moved backwards along the line the aircraft started on.</summary>
    public static double AlongTail(Pose start, Pose now)
    {
        var frame = new LocalFrame(start.Latitude, start.Longitude);
        var (x, z) = frame.ToLocal(now.Latitude, now.Longitude);
        var rad = start.Heading * Math.PI / 180;
        return -((x * Math.Sin(rad)) + (z * Math.Cos(rad)));
    }

    private void Move(PushStage stage, double now)
    {
        Stage = stage;
        _stageSince = now;
    }

    private List<string> Fail(string problem, List<string> commands)
    {
        Stage = PushStage.Failed;
        Problem = problem;
        return commands;
    }
}

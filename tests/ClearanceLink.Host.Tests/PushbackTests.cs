using ClearanceLink.Airports;
using ClearanceLink.Host.Panel;
using ClearanceLink.Host.Pushback;
using ClearanceLink.Host.Support;
using ClearanceLink.Server;

namespace ClearanceLink.Host.Tests;

public sealed class PushbackTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 5, 5, 38, 0, TimeSpan.Zero);

    // Lines as BATC 1.8.21 wrote them in Player.log on Ben's PC (2026-10-05), callsigns changed.
    [Theory]
    [InlineData("[Instruction] N123AB, Brisbane Ground, pushback and engine start approved. Face east.", 90.0, "EAST")]
    [InlineData("[Instruction] Pushback approved, Face east, N123AB.", 90.0, "EAST")]
    [InlineData("[Instruction] VHABC, Brisbane Ground, pushback and engine start approved. Face northeast.", 45.0, "NORTHEAST")]
    [InlineData("[Instruction] VHABC, pushback approved, facing south west.", 225.0, "SOUTHWEST")]
    public void Reads_the_direction_from_batcs_pushback_approval(string line, double bearing, string word)
    {
        var clearance = BatcClearance.Parse(line, At);
        Assert.NotNull(clearance);
        Assert.Equal(bearing, clearance.FaceBearing);
        Assert.Equal(word, clearance.FaceWord);
    }

    [Fact]
    public void An_approval_without_a_direction_still_counts()
    {
        var clearance = BatcClearance.Parse("[Instruction] N123AB, Brisbane Ground, push back approved.", At);
        Assert.NotNull(clearance);
        Assert.Null(clearance.FaceBearing);
    }

    [Theory]
    [InlineData("[LocalVoiceInput] sid=37 ls=1.056 | \"Guardian foxtrot alpha, brisbane ground, pushback and engine start approved. Face northeast.\"")]
    [InlineData("[Instruction] N123AB, Ramp 29, request pushback and engine start.")]
    [InlineData("[Instruction] N123AB, taxi to holding point runway 01L via A, B4.")]
    [InlineData("[GSX] Pushback selection timed out.")]
    [InlineData("[Instruction] N123AB, pushback not approved, hold position.")]
    [InlineData("[Instruction] N123AB, pushback is not yet approved, expect a short delay.")]
    public void Other_lines_are_not_approvals(string line) => Assert.Null(BatcClearance.Parse(line, At));

    [Fact]
    public void Follower_reads_only_new_lines_and_starts_over_with_a_new_log()
    {
        var path = Path.Combine(Path.GetTempPath(), $"clearancelink-player-{Guid.NewGuid():N}.log");
        try
        {
            File.WriteAllText(path, "[Instruction] N123AB, pushback approved. Face west.\n");
            var follower = new BatcLogFollower(path);
            Assert.Empty(follower.Poll()); // what was there before is history

            File.AppendAllText(path, "[Comms] Set Frequency Com1: 124050000\n[Instruction] N123AB, pushback approved. Fa");
            Assert.Empty(follower.Poll()); // half a line waits for the rest
            File.AppendAllText(path, "ce east.\r\n");
            Assert.Equal(90.0, Assert.Single(follower.Poll()).FaceBearing);
            Assert.Empty(follower.Poll());

            File.WriteAllText(path, "[Instruction] N123AB, pushback approved. Face south.\n");
            Assert.Equal(180.0, Assert.Single(follower.Poll()).FaceBearing);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A stand at YBBN-like geometry: nose 309, a taxiway 30 m behind running 039/219.
    private static readonly Pose Stand = new(-27.3864, 153.1224, 309);

    private static TaxiwaySegment Across(double behind, double bearing, string name = "B")
    {
        var frame = new LocalFrame(Stand.Latitude, Stand.Longitude);
        var tail = (Stand.Heading + 180) * Math.PI / 180;
        var (cx, cz) = (behind * Math.Sin(tail), behind * Math.Cos(tail));
        var b = bearing * Math.PI / 180;
        var (a1, a2) = (frame.FromLocal(cx - (50 * Math.Sin(b)), cz - (50 * Math.Cos(b))), frame.FromLocal(cx + (50 * Math.Sin(b)), cz + (50 * Math.Cos(b))));
        return new TaxiwaySegment(a1.Latitude, a1.Longitude, a2.Latitude, a2.Longitude, name);
    }

    [Fact]
    public void Face_east_ends_on_the_taxiway_behind_lined_up_with_it()
    {
        var plan = PushPlanner.Plan(Stand, PushChoice.Face, 90, [Across(30, 39)]);
        Assert.Equal(39, plan.FinalHeading, 1);
        Assert.Equal(90, plan.TurnDegrees, 1);
        Assert.True(plan.TailLeft);
        Assert.Equal(30 - PushPlanner.TurnRadiusMeters, plan.StraightMeters, 1);
        Assert.Equal("B", plan.Taxiway);
    }

    [Fact]
    public void Tail_keys_pick_the_taxiways_way()
    {
        var taxiways = new[] { Across(30, 39) };
        var right = PushPlanner.Plan(Stand, PushChoice.TailRight, null, taxiways);
        Assert.Equal((219.0, -90.0), (Math.Round(right.FinalHeading, 1), Math.Round(right.TurnDegrees, 1)));
        var left = PushPlanner.Plan(Stand, PushChoice.TailLeft, null, taxiways);
        Assert.Equal(39, left.FinalHeading, 1);
    }

    [Fact]
    public void Nearest_taxiway_behind_wins_and_the_stands_own_lead_in_is_ignored()
    {
        var leadIn = Across(20, 309, "lead-in"); // runs along the tail line
        var plan = PushPlanner.Plan(Stand, PushChoice.Face, 90, [Across(60, 39, "far"), leadIn, Across(35, 39, "near")]);
        Assert.Equal("near", plan.Taxiway);
    }

    [Fact]
    public void Without_a_taxiway_it_pushes_a_set_distance_and_turns_toward_the_direction()
    {
        var plan = PushPlanner.Plan(Stand, PushChoice.Face, 0, []);
        Assert.Equal(PushPlanner.DefaultStraightMeters, plan.StraightMeters);
        Assert.Equal(51, plan.TurnDegrees, 1); // 309 to 360
        Assert.Equal(PushPlanner.MaxTurnDegrees, PushPlanner.Plan(Stand, PushChoice.Face, 90, []).TurnDegrees); // 141 capped

        var straight = PushPlanner.Plan(Stand, PushChoice.Straight, null, [Across(30, 39)]);
        Assert.False(straight.Turns);
        Assert.Equal(PushPlanner.DefaultStraightMeters, straight.StraightMeters);
    }

    /// <summary>A pretend aircraft on Better Pushback's tug, moving as the Citation X did on Ben's PC.</summary>
    private sealed class Sim
    {
        public Pose Pose = Stand;
        public double Speed;
        public double Brake = 1;
        public bool Started;
        public bool Connected;
        public int Steer;

        // The nose wheel follows the steering slowly: the Citation X kept turning about 6 degrees after centring.
        private double _wheel;

        public PushInputs Inputs => new(Pose.Latitude, Pose.Longitude, Pose.Heading, Speed, Brake, Started, Connected);

        public void Apply(IEnumerable<string> commands)
        {
            foreach (var c in commands)
            {
                Steer += c switch { PushDriver.LeftCommand => -10, PushDriver.RightCommand => 10, _ => 0 };
            }
        }

        public void Move(double seconds)
        {
            // Backwards along the heading; steering right (positive) swings the tail right, so the heading falls.
            var frame = new LocalFrame(Pose.Latitude, Pose.Longitude);
            var back = (Pose.Heading + 180) * Math.PI / 180;
            var (lat, lon) = frame.FromLocal(Speed * seconds * Math.Sin(back), Speed * seconds * Math.Cos(back));
            _wheel += Math.Clamp(Steer - _wheel, -50 / 3.0 * seconds, 50 / 3.0 * seconds);
            Pose = new Pose(lat, lon, PushPlanner.Norm(Pose.Heading - (_wheel / 50.0 * 4 * seconds)));
        }
    }

    [Fact]
    public void Driver_pushes_turns_stops_and_disconnects()
    {
        var plan = PushPlanner.Plan(Stand, PushChoice.Face, 90, [Across(30, 39)]);
        var driver = new PushDriver(plan);
        var sim = new Sim();
        var sent = new List<string>();
        var t = 0.0;
        void Step()
        {
            var commands = driver.Step(sim.Inputs, t);
            sent.AddRange(commands);
            sim.Apply(commands);
        }

        Step();
        Assert.Equal([PushDriver.StartCommand], sent);
        sim.Started = true;
        Step();
        Assert.Equal(PushStage.CallingTug, driver.Stage);
        sim.Connected = true;
        Step();
        Assert.Equal(PushStage.WaitingForBrake, driver.Stage);

        // The pilot releases the brake and the tug pushes.
        sim.Brake = 0;
        sim.Speed = 1.1;
        for (; t < 200 && driver.Stage is not PushStage.Stopping; t += 0.25)
        {
            sim.Speed = sim.Steer == 0 ? 1.1 : 0.6;
            sim.Move(0.25);
            Step();
        }

        Assert.Equal(PushStage.Stopping, driver.Stage);
        Assert.Equal(5, sent.Count(c => c == PushDriver.LeftCommand)); // tail left: 50% left
        Assert.Equal(5, sent.Count(c => c == PushDriver.RightCommand)); // then centred
        Assert.Equal(0, sim.Steer);
        Assert.InRange(PushPlanner.Delta(plan.FinalHeading, sim.Pose.Heading), -4, 4);
        Assert.Equal(PushDriver.StartCommand, sent[^1]); // paused

        sim.Speed = 0;
        t += 0.25;
        Step();
        t += 1.5;
        Step();
        Assert.Equal(PushDriver.StopCommand, sent[^1]);
        Assert.Equal(PushStage.Disconnecting, driver.Stage);

        t += 1;
        Step();
        Assert.Equal(PushDriver.StopCommand, sent[^1]); // waits for the parking brake
        sim.Brake = 1;
        t += 1;
        Step();
        Assert.Equal([PushDriver.DisconnectCommand, PushDriver.AcknowledgeCommand], sent[^2..]);

        sim.Started = false;
        sim.Connected = false;
        t += 1;
        Step();
        Assert.Equal(PushStage.Done, driver.Stage);
        Assert.Null(driver.Problem);
    }

    [Fact]
    public void Stop_key_pauses_then_stops_a_running_push()
    {
        var driver = new PushDriver(new PushPlan(15, 0, 309, null));
        var sim = new Sim();
        driver.Step(sim.Inputs, 0);
        sim.Started = sim.Connected = true;
        sim.Brake = 0;
        sim.Speed = 1;
        driver.Step(sim.Inputs, 1);
        driver.Step(sim.Inputs, 2);
        Assert.Equal(PushStage.PushingBack, driver.Stage);

        driver.RequestStop();
        Assert.Equal([PushDriver.StartCommand], driver.Step(sim.Inputs, 3));
        Assert.Equal(PushStage.Stopping, driver.Stage);
    }

    [Fact]
    public void Stop_before_better_pushback_started_ends_at_once()
    {
        var driver = new PushDriver(new PushPlan(15, 0, 309, null));
        var sim = new Sim();
        driver.Step(sim.Inputs, 0);
        driver.RequestStop();
        Assert.Equal([PushDriver.StopCommand], driver.Step(sim.Inputs, 1));
        Assert.Equal(PushStage.Done, driver.Stage);
    }

    [Fact]
    public void A_flight_reload_ends_the_push_without_commands()
    {
        var driver = new PushDriver(new PushPlan(15, 0, 309, null));
        var sim = new Sim();
        driver.Step(sim.Inputs, 0);
        sim.Pose = sim.Pose with { Latitude = sim.Pose.Latitude + 0.02 }; // about 2 km
        Assert.Empty(driver.Step(sim.Inputs, 1));
        Assert.Equal((PushStage.Failed, "FLIGHT RELOADED"), (driver.Stage, driver.Problem));
    }

    [Fact]
    public void Says_so_when_the_tug_cannot_move_the_aircraft()
    {
        // The Aerobask Phenom 300 with Better Pushback 1.14: connected, brake off, nothing moves.
        var driver = new PushDriver(new PushPlan(15, 0, 309, null));
        var sim = new Sim();
        driver.Step(sim.Inputs, 0);
        sim.Started = sim.Connected = true;
        driver.Step(sim.Inputs, 1);
        sim.Brake = 0;
        driver.Step(sim.Inputs, 2);
        driver.Step(sim.Inputs, 30);
        Assert.False(driver.Stalled);
        driver.Step(sim.Inputs, 60);
        Assert.True(driver.Stalled);
    }

    [Fact]
    public void Better_pushback_not_starting_is_reported()
    {
        var driver = new PushDriver(new PushPlan(15, 0, 309, null));
        var sim = new Sim();
        driver.Step(sim.Inputs, 0);
        driver.Step(sim.Inputs, 25);
        Assert.Equal((PushStage.Failed, "BETTER PUSHBACK DIDN'T START"), (driver.Stage, driver.Problem));
    }

    private static readonly PanelState Flying = new(
        Version: "0.7.0", Serving: true, XPlaneConnected: true, XPlaneVersion: "12.4.3", BatcHelper: true, BatcApp: true, OtherAddOns: 0,
        LastAirport: "YBBN", LastAirportSource: "Orbx YBBN", MsfsRunning: false, Connection: ConnectionMode.Automatic, BatcRedirected: true,
        SessionLog: true, SessionLogBytes: 1, KeepDays: 7, Uptime: TimeSpan.FromMinutes(10), BatcFolder: @"C:\BeyondATC", XPlaneFolder: @"C:\X-Plane 12");

    [Fact]
    public void Push_page_shows_the_clearance_the_plan_and_start()
    {
        var clearance = new PushClearance(At, 90, "EAST", "pushback approved. Face east.");
        var view = new PushView(clearance, Preview: new PushPlan(21, 90, 39, "B"), XPlane: true);
        var page = CduPages.Build(PanelPage.Push, Flying with { Push = view });
        Assert.Equal(("FACE EAST", "0538Z"), (page.Rows[0].Left.Text, page.Rows[0].Right.Text));
        Assert.Equal(("21M, TAIL LEFT", "039 ON B"), (page.Rows[1].Left.Text, page.Rows[1].Right.Text));
        Assert.Equal(("<CALL TUG", CduKey.PushStart), (page.Rows[5].Left.Text, page.Rows[5].KeyLeft));
        Assert.Equal(("", CduKey.None), (page.Rows[3].Right.Text, page.Rows[3].KeyRight)); // already as ATC said
        Assert.Equal("PRESS CALL TUG", page.Scratchpad.Text);

        var other = CduPages.Build(PanelPage.Push, Flying with { Push = view with { Choice = PushChoice.Straight, Preview = new PushPlan(24, 0, 309, null) } });
        Assert.Equal(("<NO TURN", CduColor.Magenta), (other.Rows[3].Left.Text, other.Rows[3].Left.Color));
        Assert.Equal(("AS ATC SAID>", CduKey.PushAsCleared), (other.Rows[3].Right.Text, other.Rows[3].KeyRight));
        Assert.Equal(("24M, NO TURN", "309"), (other.Rows[1].Left.Text, other.Rows[1].Right.Text));
        Assert.Equal("PUSHBACK APPROVED: PUSH", CduPages.Advice(Flying with { Push = view }).Text);

        var running = CduPages.Build(PanelPage.Push, Flying with { Push = view with { Stage = PushStage.PushingBack, Pushed = 12.4 } });
        Assert.Equal(("STOP>", CduKey.PushStop), (running.Rows[5].Right.Text, running.Rows[5].KeyRight));
        Assert.Equal("PUSHING BACK 12M", running.Scratchpad.Text);

        var waiting = CduPages.Build(PanelPage.Push, Flying with { Push = new PushView(XPlane: true) });
        Assert.Equal(CduKey.None, waiting.Rows[5].KeyLeft);
        Assert.Equal("WAIT FOR PUSH CLEARANCE", waiting.Scratchpad.Text);
    }

    [Theory]
    [InlineData(PushStage.WaitingForBrake, true, false, "RELEASE PARKING BRAKE")]
    [InlineData(PushStage.WaitingForBrake, false, true, "NOT MOVING: PRESS STOP")]
    [InlineData(PushStage.Disconnecting, false, false, "SET PARKING BRAKE")]
    [InlineData(PushStage.Disconnecting, true, false, "DISCONNECTING TUG")]
    [InlineData(PushStage.Done, true, false, "PUSHBACK COMPLETE")]
    public void Push_line_says_what_to_do(PushStage stage, bool brake, bool stalled, string text) =>
        Assert.Equal(text, CduPages.PushLine(new PushView(Stage: stage, BrakeSet: brake, Stalled: stalled, XPlane: true)).Text);

    [Fact]
    public void Push_has_a_page_key()
    {
        Assert.Contains(PanelPage.Push, CduPages.MainPages);
        Assert.Equal("PUSH", CduPages.KeyName(PanelPage.Push));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Finds_out_whether_better_pushback_is_installed(bool installed)
    {
        var xplane = new InMemoryXPlane
        {
            ["sim/flightmodel/position/latitude"] = Stand.Latitude,
            ["sim/flightmodel/position/longitude"] = Stand.Longitude,
            ["sim/flightmodel/position/psi"] = Stand.Heading,
            ["sim/flightmodel/position/groundspeed"] = 0,
            ["sim/cockpit2/controls/parking_brake_ratio"] = 1,
        };
        if (installed)
        {
            xplane["bp/started"] = 0;
        }

        var pushbacks = new Pushbacks(xplane, () => [], _ => null, batcLog: null, log: _ => { }, detectAfter: TimeSpan.FromMilliseconds(300));
        Assert.Equal("", pushbacks.Choose(PushChoice.Straight));
        using var cts = new CancellationTokenSource();
        var run = pushbacks.RunAsync(cts.Token);
        Assert.False(run.IsFaulted, run.Exception?.ToString());
        for (var i = 0; i < 40 && pushbacks.View.BetterPushback is null; i++)
        {
            await Task.Delay(100);
        }

        Assert.Equal(installed, pushbacks.View.BetterPushback);
        var page = CduPages.Build(PanelPage.Push, Flying with { Push = pushbacks.View });
        if (installed)
        {
            Assert.Equal("CALLING TUG", pushbacks.Start());
            for (var i = 0; i < 20 && xplane.Commands.IsEmpty; i++)
            {
                await Task.Delay(100);
            }

            Assert.Equal(PushDriver.StartCommand, Assert.Single(xplane.Commands));
        }
        else
        {
            Assert.Equal(("", CduKey.None), (page.Rows[5].Left.Text, page.Rows[5].KeyLeft));
            Assert.Equal(("BETTER PUSHBACK MISSING", CduColor.Amber), (page.Scratchpad.Text, page.Scratchpad.Color));
            Assert.Equal("BETTER PUSHBACK MISSING", pushbacks.Start());
            await Task.Delay(600);
            Assert.Empty(xplane.Commands);
        }

        await cts.CancelAsync();
        await run;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task On_clearance_calls_the_tug_when_batc_approves(bool automatic)
    {
        var path = Path.Combine(Path.GetTempPath(), $"clearancelink-player-{Guid.NewGuid():N}.log");
        File.WriteAllText(path, "[Comms] start\n");
        var xplane = new InMemoryXPlane
        {
            ["sim/flightmodel/position/latitude"] = Stand.Latitude,
            ["sim/flightmodel/position/longitude"] = Stand.Longitude,
            ["sim/flightmodel/position/psi"] = Stand.Heading,
            ["sim/flightmodel/position/groundspeed"] = 0,
            ["sim/cockpit2/controls/parking_brake_ratio"] = 1,
            ["bp/started"] = 0,
        };
        var pushbacks = new Pushbacks(xplane, () => [], _ => null, new BatcLogFollower(path), _ => { }) { Automatic = automatic };
        using var cts = new CancellationTokenSource();
        var run = pushbacks.RunAsync(cts.Token);
        try
        {
            for (var i = 0; i < 30 && pushbacks.View.BetterPushback is null; i++)
            {
                await Task.Delay(100);
            }

            await Task.Delay(1200); // the follower has had its first look
            File.AppendAllText(path, "[Instruction] N123AB, Brisbane Ground, pushback and engine start approved. Face southwest.\n");
            for (var i = 0; i < 40 && xplane.Commands.IsEmpty && pushbacks.View.Clearance is null; i++)
            {
                await Task.Delay(100);
            }

            await Task.Delay(800);
            Assert.Equal(225.0, pushbacks.View.Clearance?.FaceBearing);
            if (automatic)
            {
                Assert.Contains(PushDriver.StartCommand, xplane.Commands);
                Assert.True(pushbacks.View.Running);
            }
            else
            {
                Assert.Empty(xplane.Commands);
                Assert.Equal("PRESS CALL TUG", CduPages.PushLine(pushbacks.View).Text);
            }
        }
        finally
        {
            await cts.CancelAsync();
            await run;
            File.Delete(path);
        }
    }

    [Fact]
    public void Push_page_switches_the_tug_between_auto_and_manual()
    {
        var auto = CduPages.Build(PanelPage.Push, Flying with { Push = new PushView(XPlane: true, Automatic: true) }).Rows[4];
        Assert.Equal(("TUG", "<AUTO", CduColor.Magenta, CduKey.PushAuto, CduKey.PushManual), (auto.LabelLeft, auto.Left.Text, auto.Left.Color, auto.KeyLeft, auto.KeyRight));
        var manual = CduPages.Build(PanelPage.Push, Flying with { Push = new PushView(XPlane: true) }).Rows[4];
        Assert.Equal(("MANUAL>", CduColor.Magenta), (manual.Right.Text, manual.Right.Color));
    }

    [Theory]
    [InlineData(null, 15)]
    [InlineData(6.0, 15)] // small GA: never less than 15 m
    [InlineData(9.6, 24)]
    [InlineData(15.6, 39)] // 737-800
    [InlineData(40.0, 60)] // capped
    public void No_turn_push_goes_about_one_aircraft_length(double? wheelbase, double meters)
    {
        Assert.Equal(meters, PushPlanner.NoTurnMeters(wheelbase));
        Assert.Equal(meters, PushPlanner.Plan(Stand, PushChoice.Straight, null, [], wheelbase).StraightMeters);
    }

    [Fact]
    public void A_new_turn_is_taken_until_the_tug_moves()
    {
        var driver = new PushDriver(new PushPlan(15, 0, 309, null));
        var sim = new Sim();
        driver.Step(sim.Inputs, 0);
        Assert.True(driver.Replan(new PushPlan(21, 90, 39, "B")));
        sim.Started = sim.Connected = true;
        sim.Brake = 0;
        sim.Speed = 1;
        driver.Step(sim.Inputs, 1);
        driver.Step(sim.Inputs, 2);
        Assert.Equal(PushStage.PushingBack, driver.Stage);
        Assert.False(driver.Replan(new PushPlan(15, 0, 309, null)));
        Assert.Equal(90, driver.Plan.TurnDegrees);
    }

    [Theory]
    [InlineData(PanelPage.Status)]
    [InlineData(PanelPage.FlightPlan)]
    [InlineData(PanelPage.Push)]
    [InlineData(PanelPage.Setup)]
    [InlineData(PanelPage.Support)]
    [InlineData(PanelPage.Traffic)]
    public void Every_labelled_row_explains_itself(PanelPage page)
    {
        var built = CduPages.Build(page, Flying with { Push = new PushView(XPlane: true) });
        Assert.All(built.Rows.Where(r => r.LabelLeft.Length + r.LabelRight.Length > 0 || r.KeyLeft is not (CduKey.None or CduKey.GoStatus or CduKey.GoSetup)),
            r => Assert.False(string.IsNullOrWhiteSpace(r.Help), $"{page} row {r.LabelLeft}/{r.Left.Text} has no help"));
    }

    [Fact]
    public void Follower_waits_for_a_character_cut_at_the_end_and_rereads_a_new_log()
    {
        var path = Path.Combine(Path.GetTempPath(), $"clearancelink-player-{Guid.NewGuid():N}.log");
        try
        {
            File.WriteAllText(path, new string('x', 500) + "\n");
            var follower = new BatcLogFollower(path);
            Assert.Empty(follower.Poll());

            // "Brisbane Ground – " with an en dash (3 UTF-8 bytes) written in two pieces.
            var line = System.Text.Encoding.UTF8.GetBytes("[Instruction] N123AB, Ground \u2013 pushback approved. Face north.\n");
            var cut = Array.IndexOf(line, (byte)0xE2) + 1;
            using (var f = new FileStream(path, FileMode.Append))
            {
                f.Write(line, 0, cut);
            }

            Assert.Empty(follower.Poll());
            using (var f = new FileStream(path, FileMode.Append))
            {
                f.Write(line, cut, line.Length - cut);
            }

            var found = Assert.Single(follower.Poll());
            Assert.Contains("\u2013", found.Text, StringComparison.Ordinal);
            Assert.Equal(0.0, found.FaceBearing);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_lost_pause_is_followed_by_a_stop()
    {
        var driver = new PushDriver(new PushPlan(15, 0, 309, null));
        var sim = new Sim();
        driver.Step(sim.Inputs, 0);
        sim.Started = sim.Connected = true;
        sim.Brake = 0;
        sim.Speed = 1;
        driver.Step(sim.Inputs, 1);
        driver.Step(sim.Inputs, 2);
        driver.RequestStop();
        Assert.Equal([PushDriver.StartCommand], driver.Step(sim.Inputs, 3)); // pause, which never arrives
        Assert.Empty(driver.Step(sim.Inputs, 6));
        Assert.Equal([PushDriver.StopCommand], driver.Step(sim.Inputs, 12));
        Assert.Empty(driver.Step(sim.Inputs, 13));
        Assert.Equal([PushDriver.StopCommand], driver.Step(sim.Inputs, 18));
    }

    [Fact]
    public void A_refused_command_stops_a_moving_push_and_drops_one_not_started()
    {
        var waiting = new PushDriver(new PushPlan(15, 0, 309, null));
        waiting.Step(new Sim().Inputs, 0);
        waiting.CommandFailed();
        Assert.Equal((PushStage.Failed, "COMMAND FAILED"), (waiting.Stage, waiting.Problem));

        var moving = new PushDriver(new PushPlan(15, 0, 309, null));
        var sim = new Sim();
        moving.Step(sim.Inputs, 0);
        sim.Started = sim.Connected = true;
        sim.Brake = 0;
        sim.Speed = 1;
        moving.Step(sim.Inputs, 1);
        moving.Step(sim.Inputs, 2);
        moving.CommandFailed();
        Assert.Equal([PushDriver.StartCommand], moving.Step(sim.Inputs, 3));
        Assert.Equal(PushStage.Stopping, moving.Stage);
    }

    [Fact]
    public void A_long_taxiway_name_still_fits_the_line()
    {
        var view = new PushView(Preview: new PushPlan(21, 90, 39, "TAXIWAY BRAVO NORTH LINK"), XPlane: true);
        var row = CduPages.Build(PanelPage.Push, Flying with { Push = view }).Rows[1];
        Assert.True(row.Left.Text.Length + row.Right.Text.Length + 1 <= CduPages.Width, $"{row.Left.Text}|{row.Right.Text}");
    }
}

using ClearanceLink.Host.Traffic;
using ClearanceLink.Server;

namespace ClearanceLink.Host.Tests;

public sealed class TrafficBridgeTests
{
    private static readonly TrafficModels Models = TrafficModels.Load(RepoFile("mapping", "traffic-models.yaml"));

    // Titles BATC created in recording 20260930-174605 (recordings/samples/batc-1.8.21-traffic.jsonl).
    [Theory]
    [InlineData("FSLTL_B738_Qantas", "B738", "QFA")]
    [InlineData("FSLTL_B738_Virgin Australia", "B738", "VOZ")]
    [InlineData("FSLTL_TFS_A332_QFA-Qantas", "A332", "QFA")]
    [InlineData("FSLTL_FAIB_A21N_JST-Jetstar Airways", "A21N", "JST")]
    [InlineData("FSLTL_FSLTL_E190_Alliance", "E190", "UTY")]
    [InlineData("FSLTL_FSLTL_SF34_Rex", "SF34", "RXA")]
    [InlineData("FSLTL_FAIB_B737_Velocity", "B737", "VOZ")]
    [InlineData("FSLTL_A320_IAE_JST_Sharklets", "A320", "JST")]
    [InlineData("FSLTL_B77W_QTR", "B77W", "QTR")]
    [InlineData("FSLTL_A388_UAE-Emirates", "A388", "UAE")]
    [InlineData("FSLTL_B738F_ZZZZ", "B738", null)]
    [InlineData("FSLTL_SF34F_ZZZZ", "SF34", null)]
    [InlineData("FSLTL_DH8D_ZZZZ", "DH8D", null)]
    [InlineData("FSLTL_FAIB_B738_Nok_Air_HS-DBY", "B738", "NOK")]
    [InlineData("FSLTL_C208B_FDX", "C208", "FDX")]
    [InlineData("Asobo PassiveAircraft King Air 350i", "B350", null)]
    [InlineData("Asobo PassiveAircraft Citation CJ4", "C25C", null)]
    [InlineData("Asobo PassiveAircraft PC12", "PC12", null)]
    [InlineData("Asobo PassiveAircraft B777-200LR", "B77L", null)]
    [InlineData("Diamond DA40NG Private Charter", "A320", null)]
    [InlineData("FSLTL_GA_DA40-NG", "DA40", null)] // drawn as an A320 on Ben's PC, 2026-10-05
    [InlineData("FSLTL_GA_Unknownplane", "C172", null)] // an unknown GA type falls back to a light aircraft
    public void Model_titles_become_icao_type_and_airline(string title, string type, string? airline) =>
        Assert.Equal(new TrafficModel(type, airline), Models.Match(title));

    [Fact]
    public void Imported_fsltl_titles_are_drawn_with_their_own_livery()
    {
        var models = TrafficModels.Load(RepoFile("mapping", "traffic-models.yaml"));
        models.UseImported([("FSLTL_B738_Qantas", "FSLTL_B738_QFA", "B738", "QFA"), ("FSLTL_B738_ZZZZ", "FSLTL_B738", "B738", "")]);

        Assert.Equal(new TrafficModel("B738", "QFA", "FSLTL_B738_QFA"), models.Match("FSLTL_B738_Qantas"));
        Assert.Equal(new TrafficModel("B738", null, "FSLTL_B738"), models.Match("FSLTL_B738_ZZZZ"));
        Assert.Equal(new TrafficModel("B738", "VOZ"), models.Match("FSLTL_B738_Virgin Australia")); // not imported: the rules

        var time = new ManualTime();
        var registry = new TrafficRegistry(time);
        var plugin = new FakePlugin { Status = Connected() };
        var bridge = new TrafficBridge(registry, plugin, models, null, _ => { }, time);
        var qantas = registry.Create(2, TrafficKind.Aircraft, "FSLTL_B738_Qantas", "", "VHXZE", new InitPosition(-27.39, 153.12, 13, 0, 0, 13, true, 0));

        bridge.Sync();

        Assert.Equal([$"add {bridge.PluginId(qantas.ObjectId)} B738 QFA FSLTL_B738_QFA VHXZE"], plugin.Adds);
    }

    [Fact]
    public void Sends_aircraft_to_the_plugin_but_not_tugs_and_removes_them_when_gone()
    {
        var time = new ManualTime();
        var registry = new TrafficRegistry(time);
        var plugin = new FakePlugin { Status = Connected() };
        var bridge = new TrafficBridge(registry, plugin, Models, null, _ => { }, time);
        var qantas = registry.Create(2, TrafficKind.Aircraft, "FSLTL_B738_Qantas", "", "VHXZE", new InitPosition(-27.39, 153.12, 13, 0, 0, 13, true, 0));
        registry.Create(2, TrafficKind.SimulatedObject, "ASO_Tug", "", "", new InitPosition(-27.39, 153.12, 13, 0, 0, 13, true, 0));

        bridge.Sync();

        var id = Assert.IsType<uint>(bridge.PluginId(qantas.ObjectId));
        Assert.InRange(id, 1u, 0xFFFFFFu);
        Assert.Equal([$"add {id} B738 QFA VHXZE"], plugin.Adds);
        Assert.Equal([(id, Models.GroundOffsetFeet("B738"))], plugin.Offsets); // sent with each add
        Assert.Single(plugin.Updates);

        time.Advance(TimeSpan.FromSeconds(1));
        bridge.Sync();
        Assert.Single(plugin.Adds); // re-sent only every ReAddInterval
        time.Advance(TrafficBridge.ReAddInterval);
        bridge.Sync();
        Assert.Equal(2, plugin.Adds.Count);

        registry.Remove(qantas.ObjectId);
        bridge.Sync();
        Assert.Equal([id], plugin.Removes);
        for (var i = 0; i < TrafficBridge.RemoveRepeats + 2; i++) bridge.Sync();
        Assert.Equal(Enumerable.Repeat(id, TrafficBridge.RemoveRepeats), plugin.Removes); // in case UDP drops a `del`
    }

    [Fact]
    public void Aircraft_are_not_sent_while_the_plugin_has_no_models_and_are_sent_once_it_has()
    {
        var time = new ManualTime();
        var registry = new TrafficRegistry(time);
        var plugin = new FakePlugin { Status = Connected() with { Models = 0 } };
        var log = new List<string>();
        var bridge = new TrafficBridge(registry, plugin, Models, null, log.Add, time);
        registry.Create(2, TrafficKind.Aircraft, "FSLTL_B738_Qantas", "", "VHXZE", new InitPosition(-27.39, 153.12, 13, 0, 0, 13, true, 0));

        bridge.Sync();
        time.Advance(TrafficBridge.ReAddInterval);
        bridge.Sync();

        Assert.Empty(plugin.Adds);
        Assert.Empty(plugin.Updates);
        Assert.Single(log, l => l.Contains("no CSL aircraft models", StringComparison.Ordinal)); // said once, not every sync

        plugin.Status = Connected(); // models installed (X-Plane restarted with a CSL package)
        bridge.Sync();
        Assert.Single(plugin.Adds);
        Assert.Single(plugin.Updates);
    }

    [Fact]
    public void Parked_aircraft_are_sent_only_when_they_change_or_every_few_seconds()
    {
        var time = new ManualTime();
        var registry = new TrafficRegistry(time);
        var plugin = new FakePlugin { Status = Connected() };
        var bridge = new TrafficBridge(registry, plugin, Models, null, _ => { }, time);
        var parked = registry.Create(2, TrafficKind.Aircraft, "FSLTL_B738_Qantas", "", "VHXZE", new InitPosition(-27.39, 153.12, 13, 0, 0, 13, true, 0));

        bridge.Sync();
        Assert.Single(plugin.Updates);

        time.Advance(TrafficBridge.Interval);
        bridge.Sync();
        Assert.Single(plugin.Updates); // nothing changed

        parked.Set("PLANE HEADING DEGREES TRUE", 20, time.GetUtcNow());
        time.Advance(TrafficBridge.Interval);
        bridge.Sync();
        Assert.Equal(2, plugin.Updates.Count); // starts to move

        time.Advance(TrafficBridge.UnchangedInterval);
        bridge.Sync();
        Assert.Equal(3, plugin.Updates.Count); // keep-alive
    }

    [Fact]
    public void A_plugin_that_fits_models_gets_the_per_type_pitch_as_a_fallback_not_in_each_position()
    {
        var models = TrafficModels.Parse("ground_pitch: {default: 0, B738: -1}");
        var time = new ManualTime();
        var registry = new TrafficRegistry(time);
        registry.Create(2, TrafficKind.Aircraft, "FSLTL_B738_Qantas", "", "VHXZE", new InitPosition(-27.39, 153.12, 13, 0, 0, 13, true, 0));

        var old = new FakePlugin { Status = Connected() };
        new TrafficBridge(registry, old, models, null, _ => { }, time).Sync();
        Assert.Null(old.Trims.Single().Pitch);
        Assert.Equal(-1, old.Updates.Single().State.Pitch);

        var fitting = new FakePlugin { Status = Connected() with { Version = "0.4.0" } };
        new TrafficBridge(registry, fitting, models, null, _ => { }, time).Sync();
        Assert.Equal(-1, fitting.Trims.Single().Pitch);
        Assert.Equal(0, fitting.Updates.Single().State.Pitch);

        Assert.False(TrafficBridge.PluginFitsModels(null));
        Assert.False(TrafficBridge.PluginFitsModels(Connected() with { Version = "0.3.3" }));
        Assert.True(TrafficBridge.PluginFitsModels(Connected() with { Version = "0.10.1" }));
    }

    [Fact]
    public void Adds_again_once_a_plugin_that_was_away_reports_back()
    {
        var time = new ManualTime();
        var registry = new TrafficRegistry(time);
        var plugin = new FakePlugin();
        var bridge = new TrafficBridge(registry, plugin, Models, null, _ => { }, time);
        registry.Create(2, TrafficKind.Aircraft, "FSLTL_B738_Qantas", "", "VHXZE", null);

        bridge.Sync();
        Assert.Empty(plugin.Adds);

        plugin.Status = Connected();
        bridge.Sync();
        Assert.Single(plugin.Adds);
    }

    [Fact]
    public void Ground_comes_from_the_plugin_under_that_aircraft_else_the_fallback()
    {
        var time = new ManualTime();
        var registry = new TrafficRegistry(time);
        var plugin = new FakePlugin { Status = Connected() };
        var bridge = new TrafficBridge(registry, plugin, Models, new FixedGround(4), _ => { }, time);
        var ai = registry.Create(2, TrafficKind.Aircraft, "FSLTL_B738_Qantas", "", "VHXZE", new InitPosition(-27.39, 153.12, 13, 0, 0, 13, true, 0));
        bridge.Sync();

        Assert.Equal(4, bridge.ElevationMeters(-27.39, 153.12)); // plugin has not measured yet
        plugin.Ground[bridge.PluginId(ai.ObjectId)!.Value] = 20;
        Assert.Equal(20 * 0.3048, bridge.ElevationMeters(-27.39, 153.12)!.Value, 6);
        Assert.Equal(4, bridge.ElevationMeters(-27.40, 153.12)); // 1 km away: not that aircraft
    }

    [Fact]
    public void State_flips_msfs_pitch_and_bank_and_reads_lights_gear_flaps_and_thrust()
    {
        var time = new ManualTime();
        var registry = new TrafficRegistry(time);
        var ai = registry.Create(2, TrafficKind.Aircraft, "FSLTL_B738_Qantas", "", "VHXZE", new InitPosition(-27.39, 153.12, 3000, -5, 10, 13, false, 180));
        var now = time.GetUtcNow();
        ai.Set("TRAILING EDGE FLAPS LEFT PERCENT", 50, now);
        ai.Set("GENERAL ENG THROTTLE LEVER POSITION:1", 80, now);
        ai.Set("LIGHT NAV", 1, now);
        ai.Set("LIGHT BEACON", 1, now);
        ai.SetEvent("LANDING_LIGHTS_SET", 1);
        ai.SetEvent("STROBES_SET", 0);
        ai.SetEvent("GEAR_DOWN", 1);
        ai.SetEvent("GEAR_UP", 1);

        var state = TrafficBridge.State(ai);

        Assert.Equal(5, state.Pitch);   // MSFS -5 is nose up
        Assert.Equal(-10, state.Bank);  // MSFS +10 is left wing down
        Assert.False(state.OnGround);
        Assert.Equal(0, state.Gear);    // GEAR_UP came last
        Assert.Equal(0.5, state.Flaps);
        Assert.Equal(0.8, state.Thrust, 6);
        Assert.Equal(TrafficLights.Nav | TrafficLights.Beacon | TrafficLights.Landing, state.Lights);

        ai.SetEvent("GEAR_DOWN", 1);
        Assert.Equal(1, TrafficBridge.State(ai).Gear);
    }

    [Fact]
    public void Wings_are_level_on_the_ground_whatever_bank_batc_writes()
    {
        var time = new ManualTime();
        var registry = new TrafficRegistry(time);
        var rolling = registry.Create(2, TrafficKind.Aircraft, "FSLTL_B738_Qantas", "", "VHXGE", new InitPosition(-27.39, 153.12, 13, 0, 6, 13, true, 120));

        Assert.Equal(0, TrafficBridge.State(rolling).Bank);
    }

    [Fact]
    public void Gear_without_any_gear_data_is_down_on_the_ground_and_low_near_an_airport()
    {
        var time = new ManualTime();
        var registry = new TrafficRegistry(time);
        var parked = registry.Create(2, TrafficKind.Aircraft, "FSLTL_B738_Qantas", "", "A", new InitPosition(-27.39, 153.12, 13, 0, 0, 13, true, 0));
        var finals = registry.Create(2, TrafficKind.Aircraft, "FSLTL_B738_Qantas", "", "B", new InitPosition(-27.30, 153.10, 1500, 0, 0, 13, false, 150));
        var cruise = registry.Create(2, TrafficKind.Aircraft, "FSLTL_B738_Qantas", "", "C", new InitPosition(-27.00, 153.00, 30000, 0, 0, 13, false, 450));

        Assert.Equal(1, TrafficBridge.State(parked).Gear);
        Assert.Equal(1, TrafficBridge.State(finals, groundFeet: 13).Gear);
        Assert.Equal(0, TrafficBridge.State(cruise, groundFeet: 13).Gear);
    }

    [Fact]
    public void Gear_is_down_on_the_ground_whatever_batc_wrote()
    {
        var time = new ManualTime();
        var registry = new TrafficRegistry(time);
        var parked = registry.Create(2, TrafficKind.Aircraft, "FSLTL_FSLTL_E190_Alliance", "", "VHUZI", new InitPosition(-27.39, 153.12, 20, 0, 0, 13, true, 0));
        parked.Set("GEAR CENTER POSITION", 0, time.GetUtcNow());
        parked.SetEvent("GEAR_UP", 1);

        Assert.Equal(1, TrafficBridge.State(parked).Gear);
    }

    [Fact]
    public void Parked_nose_is_level_but_take_off_rotation_shows()
    {
        var time = new ManualTime();
        var registry = new TrafficRegistry(time);
        var ai = registry.Create(2, TrafficKind.Aircraft, "FSLTL_B738_Qantas", "", "VHXZE", new InitPosition(-27.39, 153.12, 20, -1.5, 0, 13, true, 0));
        Assert.Equal(0, TrafficBridge.State(ai).Pitch);

        ai.Set("PLANE PITCH DEGREES", -8, time.GetUtcNow());
        ai.Set("GROUND VELOCITY", 150, time.GetUtcNow());
        Assert.Equal(8, TrafficBridge.State(ai).Pitch);
    }

    [Fact]
    public void A_stale_gear_position_write_does_not_retract_the_gear_on_approach()
    {
        var time = new ManualTime();
        var registry = new TrafficRegistry(time);
        var arriving = registry.Create(2, TrafficKind.Aircraft, "FSLTL_B738_Qantas", "", "VHXZT", new InitPosition(-27.30, 153.10, 1500, 0, 0, 13, false, 150));
        arriving.Set("GEAR CENTER POSITION", 0, time.GetUtcNow());

        Assert.Equal(1, TrafficBridge.State(arriving, groundFeet: 13).Gear);
    }

    [Fact]
    public void Ground_pitch_and_height_per_type_come_from_the_mapping()
    {
        // The plugin fits each model to the ground itself; the mapping only adjusts types that still sit wrong.
        Assert.Equal(0, Models.GroundPitch("A388"));
        Assert.Equal(0, Models.GroundOffsetFeet("A388"));
        var tuned = TrafficModels.Parse("ground_pitch: {default: 0.5, E190: -2}\nground_offset_ft: {default: 0, B738: -0.5}");
        Assert.Equal(-2, tuned.GroundPitch("E190"));
        Assert.Equal(0.5, tuned.GroundPitch("A320"));
        Assert.Equal(-0.5, tuned.GroundOffsetFeet("B738"));

        var time = new ManualTime();
        var registry = new TrafficRegistry(time);
        var parked = registry.Create(2, TrafficKind.Aircraft, "FSLTL_FSLTL_E190_Alliance", "", "VHUZI", new InitPosition(-27.39, 153.12, 20, -1.5, 0, 13, true, 0));
        Assert.Equal(-2, TrafficBridge.State(parked, groundPitch: tuned.GroundPitch("E190")).Pitch);
    }

    private static PluginStatus Connected() => new("0.1.0", 0, 100, [], DateTimeOffset.UnixEpoch);

    private static string RepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine([dir.FullName, .. parts]);
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException(Path.Combine(parts));
    }

    private sealed class FakePlugin : IPluginTraffic
    {
        public PluginStatus? Status { get; set; }

        public List<string> Adds { get; } = [];

        public List<(uint Id, TrafficState State)> Updates { get; } = [];

        public List<uint> Removes { get; } = [];

        public List<(uint Id, double Feet)> Offsets { get; } = [];

        public Dictionary<uint, double> Ground { get; } = [];

        public void Add(uint id, string icaoType, string? icaoAirline, string? livery, string label) =>
            Adds.Add($"add {id} {icaoType} {icaoAirline ?? "-"}{(livery is null ? "" : " " + livery)} {label}");

        public void Update(uint id, TrafficState state) => Updates.Add((id, state));

        public void Remove(uint id) => Removes.Add(id);

        public List<(uint Id, double? Pitch)> Trims { get; } = [];

        public void SetGroundOffset(uint id, double feet, double? pitch = null)
        {
            Offsets.Add((id, feet));
            Trims.Add((id, pitch));
        }

        public bool TryGetGroundFeet(uint id, out double feet) => Ground.TryGetValue(id, out feet);
    }

    private sealed class FixedGround(double meters) : IGroundElevation
    {
        public double? ElevationMeters(double latitude, double longitude) => meters;
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}

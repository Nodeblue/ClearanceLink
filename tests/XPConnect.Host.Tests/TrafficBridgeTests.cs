using XPConnect.Host.Traffic;
using XPConnect.Server;

namespace XPConnect.Host.Tests;

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
    public void Model_titles_become_icao_type_and_airline(string title, string type, string? airline) =>
        Assert.Equal(new TrafficModel(type, airline), Models.Match(title));

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

        public Dictionary<uint, double> Ground { get; } = [];

        public void Add(uint id, string icaoType, string? icaoAirline, string? livery, string label) =>
            Adds.Add($"add {id} {icaoType} {icaoAirline ?? "-"} {label}");

        public void Update(uint id, TrafficState state) => Updates.Add((id, state));

        public void Remove(uint id) => Removes.Add(id);

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

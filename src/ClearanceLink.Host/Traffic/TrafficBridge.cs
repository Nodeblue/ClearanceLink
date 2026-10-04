using System.Collections.Concurrent;
using ClearanceLink.Server;

namespace ClearanceLink.Host.Traffic;

/// <summary>The part of <see cref="PluginLink"/> the bridge uses; a fake in tests.</summary>
public interface IPluginTraffic
{
    PluginStatus? Status { get; }

    void Add(uint id, string icaoType, string? icaoAirline, string? livery, string label);

    void Update(uint id, TrafficState state);

    /// <summary>Per-type height (feet) and, for plugins that take it, parked pitch for models it cannot fit itself.</summary>
    void SetGroundOffset(uint id, double feet, double? pitch = null);

    void Remove(uint id);

    bool TryGetGroundFeet(uint id, out double feet);
}

/// <summary>
/// Sends BATC's AI aircraft (<see cref="TrafficRegistry"/>) to the X-Plane plugin 10 times a second (parked ones, whose
/// state has not changed, every 2 s), and answers BATC's ground height questions with the terrain the plugin finds
/// under each aircraft. Pushback tugs and other
/// non-aircraft objects are not drawn. Re-sends <c>add</c> now and then so a plugin that restarted (or an X-Plane
/// started after BATC created its traffic) still gets every aircraft.
/// </summary>
public sealed class TrafficBridge(
    TrafficRegistry registry,
    IPluginTraffic plugin,
    TrafficModels models,
    IGroundElevation? fallback,
    Action<string> log,
    TimeProvider? time = null) : IGroundElevation
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan ReAddInterval = TimeSpan.FromSeconds(10);

    /// <summary>An aircraft whose state has not changed (parked) is sent again only this often.</summary>
    public static readonly TimeSpan UnchangedInterval = TimeSpan.FromSeconds(2);

    /// <summary>A ground question is about one of our aircraft when it is this close to it.</summary>
    private const double SameAircraftMeters = 30;

    private const double FeetToMeters = 0.3048;

    /// <summary>
    /// Below this on the ground the nose is drawn level: BATC's parked pitch is MSFS's model attitude, which lifts a CSL
    /// model's nose wheel off X-Plane's ground (live test 2026-10-01). Faster, BATC's pitch shows the take-off rotation.
    /// </summary>
    private const double RotateKnots = 40;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<uint, Drawn> _drawn = new();
    // Plugin ids removed recently, and how many more syncs to send their `del` on: UDP can drop it, and an aircraft
    // the plugin never hears `del` for would stay drawn where it was.
    private readonly Dictionary<uint, int> _removed = [];
    private int _nextPluginId;
    private bool _noModels;

    /// <summary>How many syncs a removed aircraft's `del` is sent on.</summary>
    public const int RemoveRepeats = 3;

    /// <summary>The plugin's id (1..16777215) for an AI object; BATC's object ids are larger than the plugin allows.</summary>
    public uint? PluginId(uint objectId) => _drawn.TryGetValue(objectId, out var d) ? d.PluginId : null;

    public void Sync()
    {
        var now = _time.GetUtcNow();
        var aircraft = registry.Objects.Where(o => o.Kind == TrafficKind.Aircraft).ToDictionary(o => o.ObjectId);
        foreach (var (pluginId, left) in _removed.ToArray())
        {
            plugin.Remove(pluginId);
            if (left <= 1) _removed.Remove(pluginId);
            else _removed[pluginId] = left - 1;
        }

        foreach (var (objectId, drawn) in _drawn)
        {
            if (!aircraft.ContainsKey(objectId) && _drawn.TryRemove(objectId, out _))
            {
                plugin.Remove(drawn.PluginId);
                _removed[drawn.PluginId] = RemoveRepeats - 1;
            }
        }

        var status = plugin.Status;
        var connected = status is not null;
        // Without a single CSL model the plugin cannot draw anything: sending the aircraft would only make it fail
        // each one (and log it in X-Plane) every ReAddInterval. They are sent as soon as it reports models.
        var noModels = status is { Models: 0 };
        if (noModels != _noModels)
        {
            _noModels = noModels;
            if (noModels) log($"traffic: the X-Plane plugin has no CSL aircraft models; {aircraft.Count} BeyondATC aircraft are not drawn (install Bluebell or convert FSLTL)");
        }

        // Plugin 0.4.0+ parks each model on its own wheels and takes the per-type pitch only as a fallback; older ones
        // get that pitch in every position.
        var pluginFits = PluginFitsModels(status);
        foreach (var o in aircraft.Values)
        {
            var drawn = _drawn.GetOrAdd(o.ObjectId, _ => new Drawn(NextPluginId(), models.Match(o.Title)));
            if (!connected || noModels)
            {
                drawn.LastAdd = null; // add again as soon as the plugin is back, or has models
                continue;
            }

            var readded = false;
            if (drawn.LastAdd is not { } added || now - added >= ReAddInterval)
            {
                readded = true;
                if (drawn.LastAdd is null)
                {
                    log($"traffic: drawing {o.TailNumber} '{o.Title}' as {drawn.Model.IcaoType} {drawn.Model.IcaoAirline ?? "-"}{(drawn.Model.Livery is { } livery ? $" livery {livery}" : "")} (plugin id {drawn.PluginId})");
                }

                plugin.Add(drawn.PluginId, drawn.Model.IcaoType, drawn.Model.IcaoAirline, drawn.Model.Livery, o.TailNumber);
                var type = drawn.Model.IcaoType;
                plugin.SetGroundOffset(drawn.PluginId, models.GroundOffsetFeet(type), pluginFits ? models.GroundPitch(type) : null);
                drawn.LastAdd = now;
            }

            var state = State(o, fallback?.ElevationMeters(o.Latitude, o.Longitude) / FeetToMeters, pluginFits ? 0 : models.GroundPitch(drawn.Model.IcaoType));
            if (readded || state != drawn.LastState || now - drawn.LastSent >= UnchangedInterval)
            {
                plugin.Update(drawn.PluginId, state);
                drawn.LastState = state;
                drawn.LastSent = now;
            }
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    Sync();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log($"traffic: sync failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Terrain under one of our drawn aircraft, from the plugin; otherwise the fallback (nearest airport).</summary>
    public double? ElevationMeters(double latitude, double longitude) =>
        PluginGroundFeet(latitude, longitude) is { } feet ? feet * FeetToMeters : fallback?.ElevationMeters(latitude, longitude);

    /// <summary>What the plugin draws for one AI object. MSFS's pitch and bank are positive nose down and left wing down.</summary>
    public static TrafficState State(TrafficObject o, double? groundFeet = null, double groundPitch = 0)
    {
        var lights = TrafficLights.None;
        lights |= Light(o, "LIGHT NAV", "NAV_LIGHTS_SET") ? TrafficLights.Nav : 0;
        lights |= Light(o, "LIGHT BEACON", "BEACON_LIGHTS_SET") ? TrafficLights.Beacon : 0;
        lights |= Light(o, "LIGHT STROBE", "STROBES_SET") ? TrafficLights.Strobe : 0;
        lights |= Light(o, "LIGHT LANDING", "LANDING_LIGHTS_SET") ? TrafficLights.Landing : 0;
        lights |= Light(o, "LIGHT TAXI", "TAXI_LIGHTS_SET") ? TrafficLights.Taxi : 0;

        return new TrafficState(
            o.Latitude,
            o.Longitude,
            o.AltitudeFeet,
            o.HeadingTrue,
            o.OnGround && o.GroundSpeedKnots < RotateKnots ? groundPitch : -o.PitchDegrees,
            // Wings level while on the ground: BATC keeps writing a bank on the take-off roll (live test 2026-10-03, YBBN:
            // a departure drawn leaning left down the runway), which tips a model on its wheels.
            o.OnGround ? 0 : -o.BankDegrees,
            o.GroundSpeedKnots,
            o.OnGround,
            Gear(o, groundFeet),
            Flaps(o),
            Spoilers(o),
            Thrust(o),
            lights);
    }

    private static double Gear(TrafficObject o, double? groundFeet)
    {
        // Wheels are always down on the ground: BATC's gear writes and events for parked AI did not say so (live test
        // 2026-10-01, YBBN: aircraft at the gates drawn with the gear up).
        if (o.OnGround)
        {
            return 1;
        }

        // In the air: BATC's GEAR_UP / GEAR_DOWN events. Its gear position writes are not used: it writes 0 once when it
        // creates an aircraft (parked ones too) and not again, so that value goes stale.
        return o.LatestEvent("GEAR_UP", "GEAR_DOWN") switch
        {
            "GEAR_UP" => 0,
            "GEAR_DOWN" => 1,
            _ => groundFeet is { } g && o.AltitudeFeet - g < 2000 ? 1 : 0,
        };
    }

    private static double Flaps(TrafficObject o)
    {
        if (o.Has("TRAILING EDGE FLAPS LEFT PERCENT"))
        {
            return Ratio(o.Get("TRAILING EDGE FLAPS LEFT PERCENT") / 100);
        }

        return o.LatestEvent("FLAPS_UP", "FLAPS_1", "FLAPS_2", "FLAPS_3", "FLAPS_DOWN") switch
        {
            "FLAPS_1" => 0.25,
            "FLAPS_2" => 0.5,
            "FLAPS_3" => 0.75,
            "FLAPS_DOWN" => 1,
            _ => 0,
        };
    }

    private static double Spoilers(TrafficObject o) =>
        o.Has("SPOILERS LEFT POSITION")
            ? Ratio(o.Get("SPOILERS LEFT POSITION") / 100)
            : o.LatestEvent("SPOILERS_ON", "SPOILERS_OFF") == "SPOILERS_ON" ? 1 : 0;

    private static double Thrust(TrafficObject o) =>
        o.Has("GENERAL ENG THROTTLE LEVER POSITION:1") ? Ratio(o.Get("GENERAL ENG THROTTLE LEVER POSITION:1") / 100)
        : o.Has("TURB ENG N1:1") ? Ratio(o.Get("TURB ENG N1:1") / 100)
        : 0;

    /// <summary>The light's SimVar when BATC wrote it, else the last *_SET event it sent.</summary>
    private static bool Light(TrafficObject o, string simVar, string setEvent) =>
        o.Has(simVar) ? o.Get(simVar) != 0 : o.Events.TryGetValue(setEvent, out var on) && on != 0;

    private static double Ratio(double value) => Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1);

    private double? PluginGroundFeet(double latitude, double longitude)
    {
        var nearest = registry.Objects
            .Where(o => o.Kind == TrafficKind.Aircraft && _drawn.ContainsKey(o.ObjectId))
            .Select(o => (Object: o, Distance: Airports.Geo.DistanceMeters(latitude, longitude, o.Latitude, o.Longitude)))
            .Where(x => x.Distance <= SameAircraftMeters)
            .OrderBy(x => x.Distance)
            .Select(x => x.Object)
            .FirstOrDefault();
        return nearest is not null && PluginId(nearest.ObjectId) is { } id && plugin.TryGetGroundFeet(id, out var feet) ? feet : null;
    }

    /// <summary>Whether the plugin parks models on their own wheels (version 0.4.0 or later).</summary>
    public static bool PluginFitsModels(PluginStatus? status) =>
        status is not null && Version.TryParse(status.Version, out var v) && v >= new Version(0, 4, 0);

    private uint NextPluginId() => (uint)((Interlocked.Increment(ref _nextPluginId) - 1) % 0xFFFFFF) + 1;

    private sealed class Drawn(uint pluginId, TrafficModel model)
    {
        public uint PluginId { get; } = pluginId;

        public TrafficModel Model { get; } = model;

        public DateTimeOffset? LastAdd { get; set; }

        public TrafficState? LastState { get; set; }

        public DateTimeOffset LastSent { get; set; }
    }
}

using System.Collections.Concurrent;
using XPConnect.Server;

namespace XPConnect.Host.Traffic;

/// <summary>The part of <see cref="PluginLink"/> the bridge uses; a fake in tests.</summary>
public interface IPluginTraffic
{
    PluginStatus? Status { get; }

    void Add(uint id, string icaoType, string? icaoAirline, string? livery, string label);

    void Update(uint id, TrafficState state);

    void Remove(uint id);

    bool TryGetGroundFeet(uint id, out double feet);
}

/// <summary>
/// Sends BATC's AI aircraft (<see cref="TrafficRegistry"/>) to the X-Plane plugin a few times a second, and answers
/// BATC's ground height questions with the terrain the plugin finds under each aircraft. Pushback tugs and other
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
    private int _nextPluginId;

    /// <summary>The plugin's id (1..16777215) for an AI object; BATC's object ids are larger than the plugin allows.</summary>
    public uint? PluginId(uint objectId) => _drawn.TryGetValue(objectId, out var d) ? d.PluginId : null;

    public void Sync()
    {
        var now = _time.GetUtcNow();
        var aircraft = registry.Objects.Where(o => o.Kind == TrafficKind.Aircraft).ToDictionary(o => o.ObjectId);
        foreach (var (objectId, drawn) in _drawn)
        {
            if (!aircraft.ContainsKey(objectId) && _drawn.TryRemove(objectId, out _))
            {
                plugin.Remove(drawn.PluginId);
            }
        }

        var connected = plugin.Status is not null;
        foreach (var o in aircraft.Values)
        {
            var drawn = _drawn.GetOrAdd(o.ObjectId, _ => new Drawn(NextPluginId(), models.Match(o.Title)));
            if (!connected)
            {
                drawn.LastAdd = null; // add again as soon as the plugin is back
                continue;
            }

            if (drawn.LastAdd is not { } added || now - added >= ReAddInterval)
            {
                if (drawn.LastAdd is null)
                {
                    log($"traffic: drawing {o.TailNumber} '{o.Title}' as {drawn.Model.IcaoType} {drawn.Model.IcaoAirline ?? "-"} (plugin id {drawn.PluginId})");
                }

                plugin.Add(drawn.PluginId, drawn.Model.IcaoType, drawn.Model.IcaoAirline, null, o.TailNumber);
                drawn.LastAdd = now;
            }

            plugin.Update(drawn.PluginId, State(o, fallback?.ElevationMeters(o.Latitude, o.Longitude) / FeetToMeters, models.GroundPitch(drawn.Model.IcaoType)));
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
            -o.BankDegrees,
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

    private uint NextPluginId() => (uint)((Interlocked.Increment(ref _nextPluginId) - 1) % 0xFFFFFF) + 1;

    private sealed class Drawn(uint pluginId, TrafficModel model)
    {
        public uint PluginId { get; } = pluginId;

        public TrafficModel Model { get; } = model;

        public DateTimeOffset? LastAdd { get; set; }
    }
}

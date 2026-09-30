using System.Globalization;

namespace XPConnect.Server;

public enum ResolveStatus
{
    /// <summary>Value came from X-Plane (or a constant/derivation over X-Plane values).</summary>
    Ok,

    /// <summary>Mapped, but X-Plane has not delivered the dataref yet (not connected, or wrong dataref name).</summary>
    Pending,

    /// <summary>No mapping for this SimVar; zero was returned.</summary>
    Unmapped,
}

/// <summary>Produces SimVar values from X-Plane using the mapping tables, converted to the units the client asked for.</summary>
public sealed class SimVarResolver(MappingSet mappings, IXPlaneData xplane, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public MappingSet Mappings => mappings;

    /// <summary>Starts watching every dataref a SimVar needs, so values are ready by the time they are requested.</summary>
    public bool Prepare(string simVar)
    {
        if (mappings.FindSimVar(simVar, out var index) is not { } map)
        {
            return false;
        }

        foreach (var dataref in Datarefs(map, index))
        {
            xplane.Watch(dataref);
        }

        return true;
    }

    public (SimValue Value, ResolveStatus Status) Resolve(string simVar, string units)
    {
        if (mappings.FindSimVar(simVar, out var index) is not { } map)
        {
            return (SimValue.Zero, ResolveStatus.Unmapped);
        }

        if (map.ConstText is not null)
        {
            return (SimValue.Of(map.ConstText), ResolveStatus.Ok);
        }

        if (map.Const is { } constant)
        {
            return (SimValue.Of(Units.Convert(constant, map.Units, units)), ResolveStatus.Ok);
        }

        if (map.Text)
        {
            var dataref = Expand(map.Dataref ?? "", index);
            return xplane.TryGetText(dataref, out var text)
                ? (SimValue.Of(text), ResolveStatus.Ok)
                : (SimValue.Of(""), ResolveStatus.Pending);
        }

        var inputs = Datarefs(map, index).ToList();
        var values = new double[inputs.Count];
        for (var i = 0; i < inputs.Count; i++)
        {
            if (!xplane.TryGetNumber(inputs[i], out values[i]))
            {
                return (SimValue.Zero, ResolveStatus.Pending);
            }
        }

        var source = map.Derive is null
            ? values.FirstOrDefault()
            : Derivations.Apply(map.Derive, values, map, index, _time.GetUtcNow());
        return (SimValue.Of(Units.Convert(source, map.Units, units)), ResolveStatus.Ok);
    }

    private static IEnumerable<string> Datarefs(SimVarMap map, int index)
    {
        if (map.Dataref is { } d)
        {
            yield return Expand(d, index);
        }

        foreach (var input in map.Inputs)
        {
            yield return Expand(input, index);
        }
    }

    private static string Expand(string template, int index) =>
        template
            .Replace("{i}", index.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{i0}", Math.Max(0, index - 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}

/// <summary>Named calculations for SimVars that have no single X-Plane dataref. Inputs arrive in mapping order.</summary>
public static class Derivations
{
    public static double Apply(string name, double[] v, SimVarMap map, int index, DateTimeOffset now) => name switch
    {
        // a - b, e.g. GROUND ALTITUDE = elevation - height above ground
        "difference" => v[0] - v[1],

        // sign flip, e.g. MSFS pitch is positive nose-down and bank positive left; X-Plane is the opposite
        "negate" => -v[0],

        // floor at zero, e.g. X-Plane's indicated airspeed is slightly negative when parked
        "non_negative" => Math.Max(0, v[0]),

        // table lookup on the (rounded) first input; unknown values fall back to 0
        "map" => map.Map.GetValueOrDefault((int)Math.Round(v[0])),

        // 1 when the first input equals the table entry for this index (e.g. COM TRANSMIT:1 when audio_com_selection == 6)
        "equals_by_index" => map.Map.TryGetValue(index, out var wanted) && Math.Abs(v[0] - wanted) < 0.5 ? 1 : 0,

        // MSFS TIME OF DAY: 0 dawn, 1 day, 2 dusk, 3 night, from sun pitch (degrees) and local time (seconds)
        "time_of_day" => TimeOfDay(v[0], v.Length > 1 ? v[1] : 43200),

        // Zulu calendar from X-Plane day-of-year (0-based) and zulu seconds; the year is not simulated, so today's is used
        "zulu_day_of_year" => v[0] + 1,
        "zulu_day_of_week" => (double)(int)ZuluDate(v[0], now).DayOfWeek,
        "zulu_year" => now.Year,
        "absolute_time" => ZuluDate(v[0], now).Ticks / (double)TimeSpan.TicksPerSecond + v[1],

        _ => throw new InvalidOperationException($"Unknown derivation '{name}' in mapping for {map.Name}"),
    };

    private static double TimeOfDay(double sunPitchDegrees, double localSeconds) => sunPitchDegrees switch
    {
        > 6 => 1,
        > -6 => localSeconds < 43200 ? 0 : 2,
        _ => 3,
    };

    private static DateTime ZuluDate(double dayOfYearZeroBased, DateTimeOffset now) =>
        new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(Math.Clamp((int)dayOfYearZeroBased, 0, 365));
}

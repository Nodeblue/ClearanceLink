namespace XPConnect.Server;

/// <summary>
/// Converts between the unit names SimConnect clients use ("feet", "knots", "MHz", "bco16"...) and the units a mapping
/// declares for its X-Plane source. Each unit belongs to a dimension with a base unit; conversion goes through the base.
/// Unknown unit names pass values through unchanged and are reported via <see cref="IsKnown"/>.
/// </summary>
public static class Units
{
    private static readonly Dictionary<string, (string Dimension, double ToBase)> Linear = new(StringComparer.OrdinalIgnoreCase)
    {
        // length, base meter
        ["meter"] = ("length", 1), ["meters"] = ("length", 1), ["m"] = ("length", 1),
        ["foot"] = ("length", 0.3048), ["feet"] = ("length", 0.3048), ["ft"] = ("length", 0.3048),
        ["kilometer"] = ("length", 1000), ["kilometers"] = ("length", 1000), ["km"] = ("length", 1000),
        ["nautical mile"] = ("length", 1852), ["nautical miles"] = ("length", 1852), ["nmiles"] = ("length", 1852), ["nm"] = ("length", 1852),
        ["mile"] = ("length", 1609.344), ["miles"] = ("length", 1609.344),

        // angle, base degree
        ["degree"] = ("angle", 1), ["degrees"] = ("angle", 1),
        ["radian"] = ("angle", 180 / Math.PI), ["radians"] = ("angle", 180 / Math.PI),

        // speed, base m/s
        ["meters per second"] = ("speed", 1), ["meter per second"] = ("speed", 1), ["m/s"] = ("speed", 1),
        ["knot"] = ("speed", 1852.0 / 3600), ["knots"] = ("speed", 1852.0 / 3600), ["kias"] = ("speed", 1852.0 / 3600),
        ["feet per second"] = ("speed", 0.3048), ["foot per second"] = ("speed", 0.3048),
        ["feet per minute"] = ("speed", 0.3048 / 60), ["foot per minute"] = ("speed", 0.3048 / 60), ["fpm"] = ("speed", 0.3048 / 60),
        ["kilometers per hour"] = ("speed", 1 / 3.6), ["km/h"] = ("speed", 1 / 3.6),

        // frequency, base Hz
        ["hz"] = ("frequency", 1), ["hertz"] = ("frequency", 1),
        ["khz"] = ("frequency", 1e3), ["kilohertz"] = ("frequency", 1e3),
        ["mhz"] = ("frequency", 1e6), ["megahertz"] = ("frequency", 1e6),

        // time, base second
        ["second"] = ("time", 1), ["seconds"] = ("time", 1),
        ["minute"] = ("time", 60), ["minutes"] = ("time", 60),
        ["hour"] = ("time", 3600), ["hours"] = ("time", 3600),

        // ratio, base fraction 0..1
        ["percent over 100"] = ("ratio", 1), ["ratio"] = ("ratio", 1),
        ["percent"] = ("ratio", 0.01),

        // pressure, base pascal
        ["pascal"] = ("pressure", 1), ["pascals"] = ("pressure", 1),
        ["hectopascal"] = ("pressure", 100), ["hectopascals"] = ("pressure", 100),
        ["millibar"] = ("pressure", 100), ["millibars"] = ("pressure", 100), ["mbar"] = ("pressure", 100),
        ["inhg"] = ("pressure", 3386.389), ["inches of mercury"] = ("pressure", 3386.389),

        // mass, base kg
        ["kilogram"] = ("mass", 1), ["kilograms"] = ("mass", 1), ["kg"] = ("mass", 1),
        ["pound"] = ("mass", 0.45359237), ["pounds"] = ("mass", 0.45359237), ["lbs"] = ("mass", 0.45359237),
    };

    /// <summary>Unit names that carry no conversion.</summary>
    private static readonly HashSet<string> Plain = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "number", "numbers", "enum", "bool", "boolean", "mask", "flags", "position", "position 16k", "celsius",
        "string", "code",
    };

    public static bool IsKnown(string unit) =>
        Plain.Contains(Normalize(unit)) || Linear.ContainsKey(Normalize(unit)) || IsBco16(unit);

    /// <summary>Converts <paramref name="value"/> from <paramref name="from"/> units to <paramref name="to"/> units.</summary>
    public static double Convert(double value, string from, string to)
    {
        var f = Normalize(from);
        var t = Normalize(to);
        if (string.Equals(f, t, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        // Transponder codes: sources hold the four digits as a decimal number (1200); "bco16" packs them as nibbles (0x1200).
        if (IsBco16(t))
        {
            return DecimalDigitsToBco16(IsBco16(f) ? Bco16ToDecimalDigits(value) : value);
        }

        if (IsBco16(f))
        {
            return Bco16ToDecimalDigits(value);
        }

        if (Linear.TryGetValue(f, out var a) && Linear.TryGetValue(t, out var b) && a.Dimension == b.Dimension)
        {
            return value * a.ToBase / b.ToBase;
        }

        if (IsBool(t))
        {
            return value != 0 ? 1 : 0;
        }

        return value;
    }

    public static double DecimalDigitsToBco16(double code)
    {
        var n = (int)Math.Round(code);
        return ((n / 1000 % 10) << 12) | ((n / 100 % 10) << 8) | ((n / 10 % 10) << 4) | (n % 10);
    }

    public static double Bco16ToDecimalDigits(double bco)
    {
        var n = (int)Math.Round(bco);
        return ((n >> 12) & 0xF) * 1000 + ((n >> 8) & 0xF) * 100 + ((n >> 4) & 0xF) * 10 + (n & 0xF);
    }

    private static bool IsBco16(string unit) => Normalize(unit) is "bco16" or "frequency bco16";

    private static bool IsBool(string unit) => Normalize(unit) is "bool" or "boolean";

    private static string Normalize(string unit) => unit.Trim().ToLowerInvariant();
}

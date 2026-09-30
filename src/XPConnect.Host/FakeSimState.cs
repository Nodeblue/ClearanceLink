using XPConnect.Protocol;

namespace XPConnect.Host;

/// <summary>
/// User-aircraft state served by the fake simulator: a fixed snapshot of a Cirrus SF50 parked at YBBN, taken from
/// the first SimObjectData replies in the 2026-09-30 pass-through recording (docs/research/batc-call-inventory.md).
/// Clock SimVars follow the real clock, and the COM set events change the active frequencies.
/// </summary>
public sealed class FakeSimState
{
    private readonly Lock _lock = new();

    private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PLANE LATITUDE"] = -27.401607965721354,
        ["PLANE LONGITUDE"] = 153.11218271715674,
        ["INDICATED ALTITUDE CALIBRATED"] = 15.310882568359359,
        ["PRESSURE ALTITUDE"] = -358.16064453124966,
        ["PLANE ALTITUDE"] = 15.361884771048361,
        ["PLANE HEADING DEGREES TRUE"] = 296.98489466348747,
        ["PLANE HEADING DEGREES MAGNETIC"] = 285.90582837214373,
        ["AIRSPEED INDICATED"] = 0.0,
        ["GROUND VELOCITY"] = 0.0,
        ["GROUND ALTITUDE"] = 12.457501039329397,
        ["COM ACTIVE FREQUENCY:1"] = 127.85,
        ["COM ACTIVE FREQUENCY:2"] = 124.85,
        ["COM VOLUME:1"] = 1.0,
        ["COM VOLUME:2"] = 1.0,
        ["COM STATUS:1"] = 2.0,
        ["COM STATUS:2"] = 2.0,
        ["COM RECEIVE:1"] = 1.0,
        ["COM RECEIVE:2"] = 0.0,
        ["COM TRANSMIT:1"] = 1.0,
        ["COM TRANSMIT:2"] = 0.0,
        ["TRANSPONDER CODE:1"] = 1200.0,
        ["TRANSPONDER STATE:1"] = 4.0,
        ["TRANSPONDER IDENT:1"] = 0.0,
        ["CAMERA STATE"] = 2.0,
        ["SIM ON GROUND"] = 1.0,
        ["WING SPAN"] = 11.67384,
        ["GENERAL ENG COMBUSTION:1"] = 0.0,
        ["GENERAL ENG COMBUSTION:2"] = 0.0,
        ["AMBIENT WIND DIRECTION"] = 60.0,
        ["AMBIENT WIND VELOCITY"] = 13.8,
        ["DESIGN CRUISE ALT"] = 31000.0,
        ["ESTIMATED CRUISE SPEED"] = 197.0,
        ["DESIGN SPEED CLIMB"] = 165.0,
        ["DESIGN SPEED VS0"] = 67.0,
        ["DESIGN SPEED VS1"] = 86.0,
        ["DESIGN TAKEOFF SPEED"] = 95.0,
        ["TYPICAL DESCENT RATE"] = 1400.0,
        ["TIME OF DAY"] = 2.0,
        ["ATC HEAVY"] = 0.0,
        ["ATC TYPE"] = "Vision Jet",
        ["ATC MODEL"] = "SF50",
    };

    /// <summary>Hours added to UTC for LOCAL TIME. The snapshot is at YBBN (UTC+10).</summary>
    public double LocalOffsetHours { get; init; } = 10;

    public Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;

    /// <summary>Value for a SimVar, or null when the fake has none (the caller sends zeros and logs it).</summary>
    public object? Get(string name)
    {
        var now = UtcNow();
        switch (name.ToUpperInvariant())
        {
            case "ZULU TIME":
                return now.TimeOfDay.TotalSeconds;
            case "LOCAL TIME":
                return now.AddHours(LocalOffsetHours).TimeOfDay.TotalSeconds;
            case "ZULU DAY OF WEEK":
                return (double)now.DayOfWeek;
            case "ZULU DAY OF YEAR":
                return (double)now.DayOfYear;
            case "ABSOLUTE TIME":
                return now.Ticks / (double)TimeSpan.TicksPerSecond;
        }

        lock (_lock)
        {
            return _values.GetValueOrDefault(name);
        }
    }

    public void Set(string name, object value)
    {
        lock (_lock)
        {
            _values[name] = value;
        }
    }

    /// <summary>Applies a client event to the state. Returns a description when the event changed something.</summary>
    public string? ApplyEvent(string eventName, uint data)
    {
        var com = eventName.ToUpperInvariant() switch
        {
            "COM_RADIO_SET_HZ" => "COM ACTIVE FREQUENCY:1",
            "COM2_RADIO_SET_HZ" => "COM ACTIVE FREQUENCY:2",
            _ => null,
        };
        if (com is null)
        {
            return null;
        }

        var mhz = data / 1_000_000.0;
        Set(com, mhz);
        return FormattableString.Invariant($"{com}={mhz:F3}");
    }
}

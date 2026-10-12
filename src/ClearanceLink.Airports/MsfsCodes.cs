using System.Globalization;
using System.Text.RegularExpressions;

namespace ClearanceLink.Airports;

/// <summary>
/// MSFS facility enums and the conversions from X-Plane's text forms. Values were read off MSFS 2024's answers for
/// YBBN, YTWB and YBAF (docs/protocol/facility-data.md) and match the SimConnect SDK enums.
/// </summary>
public static partial class MsfsCodes
{
    /// <summary>Runway designator: 0 none, 1 left, 2 right, 3 centre, 4 water, 5 A, 6 B.</summary>
    public static int Designator(char c) => char.ToUpperInvariant(c) switch
    {
        'L' => 1,
        'R' => 2,
        'C' => 3,
        'W' => 4,
        'A' => 5,
        'B' => 6,
        _ => 0,
    };

    private static readonly string[] Compass = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    /// <summary>Splits an X-Plane runway end name ("01L", "19", "N") into the MSFS number and designator.</summary>
    public static (int Number, int Designator) Runway(string name)
    {
        name = name.Trim().ToUpperInvariant();
        var digits = 0;
        while (digits < name.Length && char.IsAsciiDigit(name[digits]))
        {
            digits++;
        }

        if (digits > 0)
        {
            var number = int.Parse(name.AsSpan(0, digits), CultureInfo.InvariantCulture);
            return (number, digits < name.Length ? Designator(name[digits]) : 0);
        }

        // Compass-named runways: MSFS numbers them 37 (N) to 44 (NW).
        var compass = Array.IndexOf(Compass, name);
        return compass >= 0 ? (37 + compass, 0) : (0, 0);
    }

    /// <summary>X-Plane apt.dat surface code to the MSFS runway SURFACE enum (0 concrete, 1 grass, 2 water, 4 asphalt...).</summary>
    public static int Surface(int xplane) => xplane switch
    {
        1 or 15 or (>= 20 and <= 38) => 4,
        2 or (>= 50 and <= 57) => 0,
        3 => 1,
        4 => 12,
        5 => 14,
        12 => 21,
        13 => 2,
        14 => 8,
        _ => 4,
    };

    /// <summary>X-Plane frequency row kind to the MSFS FREQUENCY TYPE (1 ATIS, 4 CTAF, 5 ground, 6 tower, 7 clearance, 8 approach, 9 departure).</summary>
    public static int Frequency(FrequencyKind kind) => kind switch
    {
        FrequencyKind.Atis => 1,
        FrequencyKind.Unicom => 4,
        FrequencyKind.Clearance => 7,
        FrequencyKind.Ground => 5,
        FrequencyKind.Tower => 6,
        FrequencyKind.Approach => 8,
        FrequencyKind.Departure => 9,
        _ => 0,
    };

    /// <summary>
    /// FREQUENCY TYPE for one X-Plane frequency row. X-Plane files automated weather (AWIS/AWOS/ASOS) under its
    /// ATIS row; MSFS sends it as type 12 (YTWB "1050 127050 AWIS" is MSFS type 12, recording 2026-09-30).
    /// </summary>
    public static int Frequency(Frequency frequency) =>
        frequency.Kind == FrequencyKind.Atis && IsAutomatedWeather(frequency.Name) ? AutomatedWeather : Frequency(frequency.Kind);

    public const int AutomatedWeather = 12;

    private static bool IsAutomatedWeather(string name) =>
        name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => w.ToUpperInvariant() is "AWIS" or "AWOS" or "ASOS" or "AWS");

    /// <summary>
    /// FREQUENCY NAME the way MSFS fills it, which the ATC app uses as the controller's callsign: the ICAO for ATIS, the
    /// sector for ground ("North", "Domestic"), and the city in capitals for the rest ("BRISBANE" for delivery, tower,
    /// approach and departure at YBBN). X-Plane's names are service labels ("ACD", "SMC North", "TWR EAST"), which
    /// The ATC app read out as "Acd Delivery" (2026-10-01).
    /// </summary>
    public static string FrequencyName(Frequency frequency, Airport airport)
    {
        var city = airport.Metadata.GetValueOrDefault("city") is { Length: > 0 } c ? c : airport.Name;
        switch (frequency.Kind)
        {
            case FrequencyKind.Atis:
                return airport.Icao;
            case FrequencyKind.Ground:
                var words = frequency.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(w => w.ToUpperInvariant() is not ("SMC" or "GND" or "GROUND" or "APRON" or "GRD")
                        && !city.Split(' ').Contains(w, StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                return words.Length > 0 ? string.Join(' ', words) : city.ToUpperInvariant();
            default:
                return city.ToUpperInvariant();
        }
    }
    /// <summary>TAXI_PARKING NAME: 1 PARKING, 10 GATE, 12-37 GATE_A to GATE_Z. SUFFIX uses the same values.</summary>
    public const int ParkingName = 1;
    public const int GateName = 10;

    public static int GateLetter(char letter) => 12 + (char.ToUpperInvariant(letter) - 'A');

    /// <summary>
    /// Reads an X-Plane ramp start name ("Gate 15B", "G26", "Stand 7", "GA Parking 3", "FDH") into MSFS's
    /// NAME, NUMBER and SUFFIX. The first word holding a number decides: a one-letter prefix makes GATE_x, words
    /// before it like Gate/Stand/Bay (or none, at a gate) make GATE, anything else PARKING. A name without a
    /// number gets NUMBER -1; the caller gives it a free number.
    /// </summary>
    public static (int Name, int Number, int Suffix) Parking(string xplaneName, bool isGate)
    {
        var words = xplaneName.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var match = NumberWord().Match(words[i]);
            if (!match.Success)
            {
                continue;
            }

            var number = int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture);
            var suffix = match.Groups["suffix"].Length > 0 ? GateLetter(match.Groups["suffix"].Value[0]) : 0;
            var prefix = match.Groups["prefix"].Value;
            if (prefix.Length == 1)
            {
                return (GateLetter(prefix[0]), number, suffix);
            }

            var before = words[..i];
            var gateWord = before.Any(w => w is "GATE" or "STAND" or "BAY" or "GATES" or "STANDS");
            var name = gateWord || (before.Length == 0 && prefix.Length == 0 && isGate) ? GateName : ParkingName;
            return (name, number, suffix);
        }

        return (ParkingName, -1, 0);
    }

    [GeneratedRegex(@"^(?<prefix>[A-Z]{0,2})(?<number>\d{1,4})(?<suffix>[A-Z]?)$")]
    private static partial Regex NumberWord();

    /// <summary>
    /// TAXI_PARKING TYPE from the 1300 row's type and aircraft classes and the 1301 ICAO size category:
    /// 1 RAMP_GA, 2 RAMP_GA_SMALL, 3 RAMP_GA_MEDIUM, 4 RAMP_GA_LARGE, 5 RAMP_CARGO, 6 RAMP_MIL_CARGO,
    /// 8 GATE_SMALL, 9 GATE_MEDIUM, 10 GATE_HEAVY.
    /// </summary>
    public static int ParkingType(StartLocation start)
    {
        var size = SizeCategory(start);
        if (start.Type == "gate")
        {
            return size switch { 'A' or 'B' => 8, 'C' => 9, _ => 10 };
        }

        if (start.Operation == "cargo")
        {
            return 5;
        }

        if (start.Operation == "military")
        {
            return 6;
        }

        return size switch { 'A' => 2, 'B' => 3, _ => 4 };
    }

    /// <summary>Parking radius in metres: half the largest wingspan of the size category.</summary>
    public static double ParkingRadius(StartLocation start) => SizeCategory(start) switch
    {
        'A' => 7.5,
        'B' => 12,
        'C' => 18,
        'D' => 26,
        'E' => 32.5,
        _ => 40,
    };

    /// <summary>ICAO size category A-F, from row 1301 or else guessed from the aircraft classes.</summary>
    public static char SizeCategory(StartLocation start)
    {
        if (start.SizeCategory is { Length: 1 } category && category[0] is >= 'A' and <= 'F')
        {
            return category[0];
        }

        var types = start.AircraftTypes;
        return types.Contains("heavy") ? 'E'
            : types.Contains("jets") ? 'C'
            : types.Contains("turboprops") ? 'B'
            : 'A';
    }

    /// <summary>TAXI_PATH TYPE: 2 runway, 3 parking, 4 taxiway (6 vehicle and 5 closed are not generated).</summary>
    public const int RunwayPath = 2;
    public const int ParkingPath = 3;
    public const int TaxiwayPath = 4;

    /// <summary>TAXI_POINT TYPE: 1 normal, 5 hold short.</summary>
    public const int NormalPoint = 1;
    public const int HoldShortPoint = 5;

    /// <summary>Taxiway width in metres for an apt.dat width class (A-F).</summary>
    public static double TaxiwayWidth(string widthClass) => widthClass switch
    {
        "A" => 10,
        "B" => 15,
        "C" => 18,
        "D" or "E" => 23,
        "F" => 25,
        _ => 15,
    };
}

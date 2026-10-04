using System.Globalization;

namespace ClearanceLink.Airports;

/// <summary>
/// Reads airports from X-Plane apt.dat files (format 1100/1200). Only the rows an ATC client needs are parsed;
/// pavement, lighting, signs and ground-truck routes are skipped. Unknown rows are ignored, never fatal.
/// </summary>
public static class AptDatParser
{
    /// <summary>Parses every airport in the text (a whole apt.dat or an excerpt of airport blocks).</summary>
    public static IEnumerable<Airport> Parse(TextReader reader)
    {
        Airport? current = null;
        StartLocation? lastStart = null;
        TaxiEdge? lastEdge = null;
        while (reader.ReadLine() is { } line)
        {
            var f = Split(line);
            if (f.Length == 0 || !int.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out var code))
            {
                continue;
            }

            switch (code)
            {
                case 1 or 16 or 17 when f.Length >= 5:
                    if (current is not null)
                    {
                        yield return current;
                    }

                    current = new Airport { Icao = f[4], Kind = code, ElevationFeet = D(f[1]), Name = Rest(f, 5) };
                    lastStart = null;
                    lastEdge = null;
                    break;
                case 99:
                    if (current is not null)
                    {
                        yield return current;
                    }

                    current = null;
                    break;
                case 100 when current is not null && f.Length >= 26:
                    current.Runways.Add(new Runway(D(f[1]), I(f[2]), End(f, 8), End(f, 17)));
                    break;
                case 1302 when current is not null && f.Length >= 2:
                    current.Metadata[f[1]] = Rest(f, 2);
                    break;
                case >= 1050 and <= 1056 when current is not null && f.Length >= 2:
                    current.Frequencies.Add(new Frequency((FrequencyKind)(code - 1050), I(f[1]), Rest(f, 2)));
                    break;
                case >= 50 and <= 56 when current is not null && f.Length >= 2:
                    // Legacy rows are in units of 10 kHz (11885 = 118.85 MHz).
                    current.Frequencies.Add(new Frequency((FrequencyKind)(code - 50), I(f[1]) * 10, Rest(f, 2)));
                    break;
                case 1201 when current is not null && f.Length >= 5:
                    current.TaxiNodes.Add(new TaxiNode(I(f[4]), D(f[1]), D(f[2]), f[3], Rest(f, 5)));
                    break;
                case 1202 when current is not null && f.Length >= 4:
                    var type = f.Length > 4 ? f[4] : "taxiway";
                    lastEdge = new TaxiEdge(
                        I(f[1]),
                        I(f[2]),
                        f[3] == "oneway",
                        type == "runway",
                        type.StartsWith("taxiway_", StringComparison.Ordinal) ? type[8..] : "",
                        Rest(f, 5));
                    current.TaxiEdges.Add(lastEdge);
                    break;
                case 1204 when lastEdge is not null && f.Length >= 3:
                    lastEdge.ActiveZones.Add((f[1], f[2].Split(',', StringSplitOptions.RemoveEmptyEntries)));
                    break;
                case 1300 when current is not null && f.Length >= 6:
                    lastStart = new StartLocation(D(f[1]), D(f[2]), D(f[3]), f[4], f[5].Split('|'), Rest(f, 6));
                    current.StartLocations.Add(lastStart);
                    break;
                case 1301 when lastStart is not null && f.Length >= 2:
                    lastStart.SizeCategory = f[1];
                    lastStart.Operation = f.Length > 2 ? f[2] : null;
                    lastStart.Airlines = f.Length > 3 ? f[3..] : [];
                    break;
                case 15 when current is not null && f.Length >= 4:
                    // Legacy start location without a type.
                    current.StartLocations.Add(new StartLocation(D(f[1]), D(f[2]), D(f[3]), "misc", ["all"], Rest(f, 4)));
                    break;
            }
        }

        if (current is not null)
        {
            yield return current;
        }
    }

    public static Airport? Find(TextReader reader, string icao) =>
        Parse(reader).FirstOrDefault(a => a.Icao.Equals(icao, StringComparison.OrdinalIgnoreCase));

    private static RunwayEnd End(string[] f, int i) =>
        new(f[i], D(f[i + 1]), D(f[i + 2]), D(f[i + 3]), D(f[i + 4]), I(f[i + 5]), I(f[i + 6]), I(f[i + 7]) != 0, I(f[i + 8]));

    private static string[] Split(string line) => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static string Rest(string[] f, int from) => from < f.Length ? string.Join(' ', f[from..]) : "";

    private static double D(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (int)Math.Round(D(s));
}

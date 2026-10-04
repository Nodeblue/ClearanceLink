using System.Globalization;

namespace ClearanceLink.Airports;

/// <summary>A fix or navaid from X-Plane's earth_fix.dat / earth_nav.dat. Kind is 'W' fix, 'V' VOR, 'N' NDB, 'D' DME, 'L' localizer.</summary>
public sealed record NavPoint(string Ident, string Region, string Terminal, char Kind, double Latitude, double Longitude, double Variation);

/// <summary>
/// Fixes and navaids from X-Plane's nav data (format 1100/1200), for procedure fix positions and, from VORs,
/// the local magnetic variation. Unknown rows are skipped.
/// </summary>
public sealed class NavData
{
    private readonly Dictionary<string, List<NavPoint>> _byIdent = new(StringComparer.Ordinal);
    private readonly List<NavPoint> _vors = [];

    public int Count { get; private set; }

    public static NavData Load(TextReader? fixes, TextReader? navaids)
    {
        var data = new NavData();
        if (fixes is not null)
        {
            data.ReadFixes(fixes);
        }

        if (navaids is not null)
        {
            data.ReadNavaids(navaids);
        }

        return data;
    }

    public static NavData Load(string? fixPath, string? navPath)
    {
        using var fixes = fixPath is not null && File.Exists(fixPath) ? File.OpenText(fixPath) : null;
        using var navaids = navPath is not null && File.Exists(navPath) ? File.OpenText(navPath) : null;
        return Load(fixes, navaids);
    }

    /// <summary>
    /// The point a procedure names: same ident and region, the preferred kind first, then one in the airport's
    /// terminal area, then the nearest to the airport.
    /// </summary>
    public NavPoint? Find(string ident, string region, char kind, string airport, double latitude, double longitude)
    {
        if (!_byIdent.TryGetValue(ident, out var list))
        {
            return null;
        }

        return list
            .OrderBy(p => p.Region == region ? 0 : 1)
            .ThenBy(p => p.Kind == kind ? 0 : 1)
            .ThenBy(p => p.Terminal == airport ? 0 : 1)
            .ThenBy(p => Geo.DistanceMeters(latitude, longitude, p.Latitude, p.Longitude))
            .First();
    }

    /// <summary>Magnetic variation (east positive) of the nearest VOR within 300 km, as X-Plane slaves it.</summary>
    public double? Variation(double latitude, double longitude)
    {
        NavPoint? best = null;
        var bestDistance = 300_000.0;
        foreach (var vor in _vors)
        {
            if (Math.Abs(vor.Latitude - latitude) > 3)
            {
                continue;
            }

            var distance = Geo.DistanceMeters(latitude, longitude, vor.Latitude, vor.Longitude);
            if (distance < bestDistance)
            {
                (best, bestDistance) = (vor, distance);
            }
        }

        return best?.Variation;
    }

    private void Add(NavPoint point)
    {
        if (!_byIdent.TryGetValue(point.Ident, out var list))
        {
            _byIdent[point.Ident] = list = [];
        }

        list.Add(point);
        Count++;
    }

    // earth_fix.dat 1200: lat lon ident terminal-area(ENRT or airport) region type [name]
    private void ReadFixes(TextReader reader)
    {
        while (reader.ReadLine() is { } line)
        {
            var f = line.Split((char[]?)null, 6, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length >= 5 && TryD(f[0], out var lat) && TryD(f[1], out var lon))
            {
                Add(new NavPoint(f[2], f[4], f[3], 'W', lat, lon, 0));
            }
        }
    }

    // earth_nav.dat 1200: type lat lon elevation frequency range variation/bearing ident terminal region name
    private void ReadNavaids(TextReader reader)
    {
        while (reader.ReadLine() is { } line)
        {
            var f = line.Split((char[]?)null, 11, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 10 || !int.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out var type) ||
                !TryD(f[1], out var lat) || !TryD(f[2], out var lon))
            {
                continue;
            }

            char kind = type switch
            {
                2 => 'N',
                3 => 'V',
                4 or 5 => 'L',
                12 or 13 => 'D',
                _ => ' ',
            };
            if (kind == ' ')
            {
                continue;
            }

            TryD(f[6], out var variation);
            var point = new NavPoint(f[7], f[9], f[8], kind, lat, lon, kind == 'V' ? variation : 0);
            Add(point);
            if (kind == 'V')
            {
                _vors.Add(point);
            }
        }
    }

    private static bool TryD(string s, out double value) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

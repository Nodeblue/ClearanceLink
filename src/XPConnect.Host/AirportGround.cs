using XPConnect.Protocol.Facilities;
using XPConnect.Server;

namespace XPConnect.Host;

/// <summary>
/// Ground height for BATC's AI traffic: the elevation of the nearest airport XPConnect has sent BATC, within a few
/// kilometres. BATC keeps its traffic on the ground with it, and its traffic is almost always at those airports.
/// Away from them the session falls back to the ground under the user's aircraft.
/// </summary>
public sealed class AirportGround : IGroundElevation
{
    private const double RangeMeters = 8000;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, (double Lat, double Lon, double ElevationMeters)> _airports = new(StringComparer.OrdinalIgnoreCase);

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _airports.Count;
            }
        }
    }

    /// <summary>Remembers an airport from its built AIRPORT facility object (ALTITUDE is metres).</summary>
    public void Add(string icao, FacilityObject airport)
    {
        if (airport["LATITUDE"] is not { } lat || airport["LONGITUDE"] is not { } lon || airport["ALTITUDE"] is not { } alt)
        {
            return;
        }

        Add(icao, Convert.ToDouble(lat, System.Globalization.CultureInfo.InvariantCulture), Convert.ToDouble(lon, System.Globalization.CultureInfo.InvariantCulture), Convert.ToDouble(alt, System.Globalization.CultureInfo.InvariantCulture));
    }

    public void Add(string icao, double latitude, double longitude, double elevationMeters)
    {
        lock (_gate)
        {
            _airports[icao] = (latitude, longitude, elevationMeters);
        }
    }

    public double? ElevationMeters(double latitude, double longitude)
    {
        lock (_gate)
        {
            return _airports.Values
                .Select(a => (Elevation: (double?)a.ElevationMeters, Distance: Airports.Geo.DistanceMeters(latitude, longitude, a.Lat, a.Lon)))
                .Where(a => a.Distance <= RangeMeters)
                .OrderBy(a => a.Distance)
                .Select(a => a.Elevation)
                .FirstOrDefault();
        }
    }
}

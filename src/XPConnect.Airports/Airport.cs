namespace XPConnect.Airports;

/// <summary>
/// One airport as X-Plane describes it in apt.dat (format 1200+), reduced to what an ATC client needs.
/// Units follow apt.dat: degrees, meters for lengths, feet for elevation, kHz for frequencies.
/// </summary>
public sealed class Airport
{
    public required string Icao { get; init; }

    public string Name { get; set; } = "";

    public double ElevationFeet { get; set; }

    /// <summary>1 land airport, 16 seaplane base, 17 heliport (the apt.dat header row code).</summary>
    public int Kind { get; set; } = 1;

    /// <summary>Row 1302 metadata: city, country, datum_lat, datum_lon, icao_code, iata_code, region_code, transition_alt...</summary>
    public Dictionary<string, string> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<Runway> Runways { get; } = [];

    public List<Frequency> Frequencies { get; } = [];

    public List<TaxiNode> TaxiNodes { get; } = [];

    public List<TaxiEdge> TaxiEdges { get; } = [];

    public List<StartLocation> StartLocations { get; } = [];

    /// <summary>Reference point: the 1302 datum if present, otherwise the middle of the runways.</summary>
    public (double Latitude, double Longitude) ReferencePoint
    {
        get
        {
            if (TryMeta("datum_lat", out var lat) && TryMeta("datum_lon", out var lon))
            {
                return (lat, lon);
            }

            var ends = Runways.SelectMany(r => new[] { r.End1, r.End2 }).ToList();
            return ends.Count == 0
                ? (StartLocations.FirstOrDefault()?.Latitude ?? 0, StartLocations.FirstOrDefault()?.Longitude ?? 0)
                : (ends.Average(e => e.Latitude), ends.Average(e => e.Longitude));
        }
    }

    public string? Region => Metadata.GetValueOrDefault("region_code");

    private bool TryMeta(string key, out double value)
    {
        value = 0;
        return Metadata.TryGetValue(key, out var text) &&
            double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>apt.dat row 100. Surface codes: 1 asphalt, 2 concrete, 3 grass, 4 dirt, 5 gravel, 12 dry lakebed, 13 water, 14 snow/ice, 15 transparent, 20-38 and 50-57 asphalt/concrete variants.</summary>
public sealed record Runway(double WidthMeters, int Surface, RunwayEnd End1, RunwayEnd End2)
{
    public string Name => $"{End1.Number}/{End2.Number}";

    public double LengthMeters => Geo.DistanceMeters(End1.Latitude, End1.Longitude, End2.Latitude, End2.Longitude);
}

/// <summary>One end of a runway. Latitude/longitude is the runway end, not the displaced threshold.</summary>
public sealed record RunwayEnd(
    string Number,
    double Latitude,
    double Longitude,
    double DisplacedThresholdMeters,
    double BlastpadMeters,
    int Markings,
    int ApproachLights,
    bool TouchdownZoneLights,
    int Reil);

/// <summary>apt.dat rows 1050-1056 (8.33 kHz era) or 50-56 (legacy, converted to kHz).</summary>
public sealed record Frequency(FrequencyKind Kind, int Khz, string Name);

public enum FrequencyKind
{
    Atis = 0,
    Unicom = 1,
    Clearance = 2,
    Ground = 3,
    Tower = 4,
    Approach = 5,
    Departure = 6,
}

/// <summary>apt.dat row 1201. Usage is dest, init, both or junc.</summary>
public sealed record TaxiNode(int Id, double Latitude, double Longitude, string Usage, string Name);

/// <summary>apt.dat rows 1202 (edge) and 1204 (active zones on it).</summary>
public sealed record TaxiEdge(int From, int To, bool OneWay, bool IsRunway, string WidthClass, string Name)
{
    /// <summary>Runways whose protected zone this edge crosses, by use: departure, arrival, ils.</summary>
    public List<(string Use, string[] Runways)> ActiveZones { get; } = [];
}

/// <summary>apt.dat row 1300, with its 1301 metadata when present.</summary>
public sealed record StartLocation(double Latitude, double Longitude, double Heading, string Type, string[] AircraftTypes, string Name)
{
    /// <summary>ICAO width category A-F (row 1301).</summary>
    public string? SizeCategory { get; set; }

    /// <summary>none, general_aviation, airline, cargo or military (row 1301).</summary>
    public string? Operation { get; set; }

    /// <summary>Three-letter airline codes (row 1301).</summary>
    public string[] Airlines { get; set; } = [];
}

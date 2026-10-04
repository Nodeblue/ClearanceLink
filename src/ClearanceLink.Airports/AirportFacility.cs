using ClearanceLink.Protocol.Facilities;

namespace ClearanceLink.Airports;

/// <summary>What X-Plane's airport file does not say, from other X-Plane data (nav data, CIFP).</summary>
public sealed class AirportExtras
{
    /// <summary>Magnetic variation in degrees, east positive.</summary>
    public double? MagneticVariationEast { get; init; }

    /// <summary>ILS identifier per runway end name ("01L" -> "IBN").</summary>
    public IReadOnlyDictionary<string, string> Ils { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<FacilityObject> Approaches { get; init; } = [];

    public IReadOnlyList<FacilityObject> Departures { get; init; } = [];

    public IReadOnlyList<FacilityObject> Arrivals { get; init; } = [];
}

/// <summary>
/// Builds the facility object tree MSFS would send for an airport from X-Plane's description of it. Every field a
/// client can ask for is set when X-Plane has it; the encoder then sends only what the client's definition names.
/// Conventions are in docs/protocol/facility-data.md.
/// </summary>
public static class AirportFacility
{
    private const double FeetToMeters = 0.3048;

    public static FacilityObject Build(Airport airport, AirportExtras? extras = null, TaxiNetwork? taxi = null)
    {
        extras ??= new AirportExtras();
        taxi ??= TaxiNetwork.Build(airport);
        var (lat, lon) = airport.ReferencePoint;
        var frame = new LocalFrame(lat, lon);
        var elevation = airport.ElevationFeet * FeetToMeters;

        var root = new FacilityObject("AIRPORT")
            .Set("LATITUDE", lat)
            .Set("LONGITUDE", lon)
            .Set("ALTITUDE", elevation)
            .Set("NAME", airport.Name)
            .Set("NAME64", airport.Name)
            .Set("ICAO", airport.Metadata.GetValueOrDefault("icao_code") ?? airport.Icao)
            .Set("REGION", airport.Region ?? "");

        // MSFS stores variation west-positive in 0-360: 11 degrees east is 349 (YTWB, YBAF).
        if (extras.MagneticVariationEast is { } east)
        {
            root.Set("MAGVAR", ((-east % 360) + 360) % 360);
        }

        if (TryFeet(airport, "transition_alt", out var transitionAltitude))
        {
            root.Set("TRANSITION_ALTITUDE", transitionAltitude * FeetToMeters);
        }

        if (TryFeet(airport, "transition_level", out var transitionLevel))
        {
            // Written as feet (11000) or as a flight level (110) depending on the scenery author.
            root.Set("TRANSITION_LEVEL", (transitionLevel < 1000 ? transitionLevel * 100 : transitionLevel) * FeetToMeters);
        }

        foreach (var runway in airport.Runways)
        {
            root.Add(Runway(runway, elevation, extras));
        }

        foreach (var runway in airport.Runways)
        {
            foreach (var end in new[] { runway.End1, runway.End2 })
            {
                var (number, designator) = MsfsCodes.Runway(end.Number);
                var other = end == runway.End1 ? runway.End2 : runway.End1;
                root.Add(new FacilityObject("START")
                    .Set("LATITUDE", end.Latitude)
                    .Set("LONGITUDE", end.Longitude)
                    .Set("ALTITUDE", elevation)
                    .Set("HEADING", Geo.BearingDegrees(end.Latitude, end.Longitude, other.Latitude, other.Longitude))
                    .Set("NUMBER", number)
                    .Set("DESIGNATOR", designator)
                    .Set("TYPE", 1));
            }
        }

        foreach (var frequency in airport.Frequencies)
        {
            root.Add(new FacilityObject("FREQUENCY")
                .Set("TYPE", MsfsCodes.Frequency(frequency))
                .Set("FREQUENCY", frequency.Khz * 1000)
                .Set("NAME", MsfsCodes.FrequencyName(frequency, airport)));
        }

        foreach (var spot in taxi.Parking)
        {
            var (x, z) = frame.ToLocal(spot.Start.Latitude, spot.Start.Longitude);
            root.Add(new FacilityObject("TAXI_PARKING")
                .Set("TYPE", spot.Type)
                .Set("NAME", spot.Name)
                .Set("SUFFIX", spot.Suffix)
                .Set("NUMBER", spot.Number)
                .Set("HEADING", spot.Heading)
                .Set("RADIUS", spot.Radius)
                .Set("BIAS_X", x)
                .Set("BIAS_Z", z));
        }

        foreach (var name in taxi.Names)
        {
            root.Add(new FacilityObject("TAXI_NAME").Set("NAME", name));
        }

        foreach (var path in taxi.Paths)
        {
            root.Add(new FacilityObject("TAXI_PATH")
                .Set("TYPE", path.Type)
                .Set("WIDTH", path.Width)
                .Set("LEFT_HALF_WIDTH", path.Width / 2)
                .Set("RIGHT_HALF_WIDTH", path.Width / 2)
                .Set("RUNWAY_NUMBER", path.RunwayNumber)
                .Set("RUNWAY_DESIGNATOR", path.RunwayDesignator)
                .Set("START", path.Start)
                .Set("END", path.End)
                .Set("NAME_INDEX", path.NameIndex));
        }

        foreach (var point in taxi.Points)
        {
            var (x, z) = frame.ToLocal(point.Latitude, point.Longitude);
            root.Add(new FacilityObject("TAXI_POINT").Set("TYPE", point.Type).Set("BIAS_X", x).Set("BIAS_Z", z));
        }

        foreach (var approach in extras.Approaches)
        {
            root.Add(approach);
        }

        foreach (var departure in extras.Departures)
        {
            root.Add(departure);
        }

        foreach (var arrival in extras.Arrivals)
        {
            root.Add(arrival);
        }

        return root
            .Set("N_RUNWAYS", root.ChildrenOf("RUNWAY").Count)
            .Set("N_STARTS", root.ChildrenOf("START").Count)
            .Set("N_FREQUENCIES", root.ChildrenOf("FREQUENCY").Count)
            .Set("N_APPROACHES", root.ChildrenOf("APPROACH").Count)
            .Set("N_DEPARTURES", root.ChildrenOf("DEPARTURE").Count)
            .Set("N_ARRIVALS", root.ChildrenOf("ARRIVAL").Count)
            .Set("N_TAXI_POINTS", taxi.Points.Count)
            .Set("N_TAXI_PARKINGS", taxi.Parking.Count)
            .Set("N_TAXI_PATHS", taxi.Paths.Count)
            .Set("N_TAXI_NAMES", taxi.Names.Count);
    }

    private static FacilityObject Runway(Runway runway, double elevation, AirportExtras extras)
    {
        var primaryFirst = TaxiNetwork.End1IsPrimary(runway);
        var primary = primaryFirst ? runway.End1 : runway.End2;
        var secondary = primaryFirst ? runway.End2 : runway.End1;
        var (primaryNumber, primaryDesignator) = MsfsCodes.Runway(primary.Number);
        var (secondaryNumber, secondaryDesignator) = MsfsCodes.Runway(secondary.Number);
        var obj = new FacilityObject("RUNWAY")
            .Set("LATITUDE", (primary.Latitude + secondary.Latitude) / 2)
            .Set("LONGITUDE", (primary.Longitude + secondary.Longitude) / 2)
            .Set("ALTITUDE", elevation)
            .Set("HEADING", Geo.BearingDegrees(primary.Latitude, primary.Longitude, secondary.Latitude, secondary.Longitude))
            .Set("LENGTH", runway.LengthMeters)
            .Set("WIDTH", runway.WidthMeters)
            .Set("PATTERN_ALTITUDE", 1000 * FeetToMeters)
            .Set("SURFACE", MsfsCodes.Surface(runway.Surface))
            .Set("PRIMARY_NUMBER", primaryNumber)
            .Set("PRIMARY_DESIGNATOR", primaryDesignator)
            .Set("SECONDARY_NUMBER", secondaryNumber)
            .Set("SECONDARY_DESIGNATOR", secondaryDesignator);
        if (extras.Ils.TryGetValue(primary.Number, out var primaryIls))
        {
            obj.Set("PRIMARY_ILS_ICAO", primaryIls);
        }

        if (extras.Ils.TryGetValue(secondary.Number, out var secondaryIls))
        {
            obj.Set("SECONDARY_ILS_ICAO", secondaryIls);
        }

        return obj;
    }

    private static bool TryFeet(Airport airport, string key, out double value)
    {
        value = 0;
        return airport.Metadata.TryGetValue(key, out var text) &&
            double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) &&
            value > 0;
    }
}

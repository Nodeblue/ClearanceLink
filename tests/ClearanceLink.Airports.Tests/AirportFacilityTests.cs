using ClearanceLink.Protocol;
using ClearanceLink.Protocol.Facilities;

namespace ClearanceLink.Airports.Tests;

public sealed class AirportFacilityTests
{
    // The protocol version the ATC app opens with (only written into packet headers here).
    private const uint Protocol = 6;

    [Theory]
    [InlineData("Gate 24", true, 10, 24, 0)]
    [InlineData("Gate 15B", true, 10, 15, 13)]
    [InlineData("GAte 103", true, 10, 103, 0)]
    [InlineData("G26", false, 18, 26, 0)]
    [InlineData("Stand 7", false, 10, 7, 0)]
    [InlineData("Bay 2 RFDS", false, 10, 2, 0)]
    [InlineData("GA Parking 3", false, 1, 3, 0)]
    [InlineData("12", true, 10, 12, 0)]
    [InlineData("12", false, 1, 12, 0)]
    [InlineData("FDH", false, 1, -1, 0)]
    [InlineData("GA iteinerant", false, 1, -1, 0)]
    public void Parking_names_become_msfs_name_number_and_suffix(string xplane, bool isGate, int name, int number, int suffix) =>
        Assert.Equal((name, number, suffix), MsfsCodes.Parking(xplane, isGate));

    [Theory]
    [InlineData("01L", 1, 1)]
    [InlineData("19R", 19, 2)]
    [InlineData("36C", 36, 3)]
    [InlineData("06", 6, 0)]
    [InlineData("NE", 38, 0)]
    public void Runway_names_become_number_and_designator(string name, int number, int designator) =>
        Assert.Equal((number, designator), MsfsCodes.Runway(name));

    [Fact]
    public void Ybbn_runways_and_starts_match_msfs()
    {
        var ours = Encoded("YBBN");
        var msfs = Fixtures.Msfs("YBBN");

        foreach (var theirs in msfs.ChildrenOf("RUNWAY"))
        {
            var mine = ours.ChildrenOf("RUNWAY").Single(r =>
                Same(r, theirs, "PRIMARY_NUMBER") && Same(r, theirs, "PRIMARY_DESIGNATOR") &&
                Same(r, theirs, "SECONDARY_NUMBER") && Same(r, theirs, "SECONDARY_DESIGNATOR"));
            Assert.InRange(Metres(mine, theirs), 0, 100);
            Assert.InRange(Math.Abs(Num(mine, "HEADING") - Num(theirs, "HEADING")), 0, 0.5);
            Assert.InRange(Math.Abs(Num(mine, "LENGTH") - Num(theirs, "LENGTH")), 0, 100);
            Assert.Equal(Num(theirs, "SURFACE"), Num(mine, "SURFACE"));
        }

        foreach (var theirs in msfs.ChildrenOf("START").Where(s => Num(s, "DESIGNATOR") != 0 || Num(s, "NUMBER") > 2))
        {
            var mine = ours.ChildrenOf("START").Single(s => Same(s, theirs, "NUMBER") && Same(s, theirs, "DESIGNATOR"));
            Assert.InRange(Metres(mine, theirs), 0, 100);
        }
    }

    [Fact]
    public void Ybbn_frequencies_use_msfs_types_in_hz()
    {
        var ours = Encoded("YBBN").ChildrenOf("FREQUENCY").Select(f => ((int)Num(f, "TYPE"), (int)Num(f, "FREQUENCY"))).ToList();
        var msfs = Fixtures.Msfs("YBBN").ChildrenOf("FREQUENCY").Select(f => ((int)Num(f, "TYPE"), (int)Num(f, "FREQUENCY"))).ToHashSet();

        Assert.NotEmpty(ours);
        Assert.Contains((5, 121700000), ours);
        Assert.Contains((6, 120500000), ours);
        Assert.True(ours.Count(msfs.Contains) >= ours.Count - 2, "most X-Plane frequencies should appear in the MSFS list");
    }

    [Fact]
    public void Ybbn_parking_sits_where_msfs_puts_it()
    {
        var ours = Encoded("YBBN");
        var msfs = Fixtures.Msfs("YBBN");

        // Biases are relative to each answer's own airport reference point, so compare in one frame.
        var frame = new LocalFrame(Num(ours, "LATITUDE"), Num(ours, "LONGITUDE"));
        var msfsFrame = new LocalFrame(Num(msfs, "LATITUDE"), Num(msfs, "LONGITUDE"));
        var mine = ours.ChildrenOf("TAXI_PARKING").Select(Bias).ToList();
        var nearest = msfs.ChildrenOf("TAXI_PARKING")
            .Select(p => msfsFrame.FromLocal(Num(p, "BIAS_X"), Num(p, "BIAS_Z")))
            .Select(p => frame.ToLocal(p.Latitude, p.Longitude))
            .Select(p => mine.Min(m => Distance(m, p)))
            .Order()
            .ToList();

        Assert.InRange(nearest[nearest.Count / 2], 0, 20);
        Assert.Contains(ours.ChildrenOf("TAXI_PARKING"), p => Num(p, "NAME") == 10 && Num(p, "NUMBER") == 24);
    }

    [Fact]
    public void Ybbn_taxi_network_is_consistent()
    {
        var ours = Encoded("YBBN");
        var points = ours.ChildrenOf("TAXI_POINT");
        var paths = ours.ChildrenOf("TAXI_PATH");
        var parking = ours.ChildrenOf("TAXI_PARKING");
        var names = ours.ChildrenOf("TAXI_NAME").Select(n => (string)n["NAME"]!).ToList();

        Assert.Equal(points.Count, Num(ours, "N_TAXI_POINTS"));
        Assert.Equal(paths.Count, Num(ours, "N_TAXI_PATHS"));
        Assert.Equal("", names[0]);
        Assert.Contains("A", names);
        Assert.Contains(points, p => Num(p, "TYPE") == 5);
        foreach (var path in paths)
        {
            Assert.InRange(Num(path, "START"), 0, points.Count - 1);
            Assert.InRange(Num(path, "END"), 0, (Num(path, "TYPE") == 3 ? parking.Count : points.Count) - 1);
            Assert.InRange(Num(path, "NAME_INDEX"), 0, names.Count - 1);
        }

        // Each spot has a taxi point of its own: The ATC app's route builder fails to load an airport where spots share one.
        var starts = paths.Where(p => Num(p, "TYPE") == 3).Select(p => Num(p, "START")).ToList();
        Assert.Equal(starts.Count, starts.Distinct().Count());
        Assert.All(starts, s => Assert.Contains(paths, p => Num(p, "TYPE") != 3 && (Num(p, "START") == s || Num(p, "END") == s)));
        Assert.All(paths.Where(p => Num(p, "TYPE") == 3), p =>
            Assert.InRange(Distance(Bias(points[(int)Num(p, "START")]), Bias(parking[(int)Num(p, "END")])), 4.99, 500));

        // Every parking spot is joined to the network exactly once.
        Assert.Equal(
            Enumerable.Range(0, parking.Count).Select(i => (double)i),
            paths.Where(p => Num(p, "TYPE") == 3).Select(p => Num(p, "END")).Order());

        // Runway paths are tagged with the nearer runway end, so every end appears, as in MSFS's YBBN answer.
        var runwayPaths = paths.Where(p => Num(p, "TYPE") == 2).Select(p => (Num(p, "RUNWAY_NUMBER"), Num(p, "RUNWAY_DESIGNATOR"))).ToHashSet();
        var msfsPaths = Fixtures.Msfs("YBBN").ChildrenOf("TAXI_PATH").Where(p => Num(p, "TYPE") == 2)
            .Select(p => (Num(p, "RUNWAY_NUMBER"), Num(p, "RUNWAY_DESIGNATOR"))).ToHashSet();
        Assert.Equal(msfsPaths.Order(), runwayPaths.Order());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_generated_network_for_a_strip_with_few_spots_has_no_coincident_points(int spots)
    {
        // With one spot the average of the spots is the spot itself, which put the apron point and the spot's own
        // point on top of each other (two points at one position crash the app's 1.8.21).
        var gates = string.Join("\n", Enumerable.Range(0, spots).Select(i => $"1300 -27.4010{i}000 153.09900000 90.00 tie_down props GA {i + 1}"));
        var text = $"""
            I
            1200 Version
            1 13 0 0 YONE One Strip
            100 30.00 1 0 0.25 1 2 1 01 -27.41000000 153.10000000 0 0 3 5 1 1 19 -27.39000000 153.10500000 0 0 3 0 0 0
            {gates}
            99
            """;
        var airport = Assert.Single(AptDatParser.Parse(new StringReader(text)));
        var network = TaxiNetwork.Build(airport);

        Assert.True(network.Generated);
        Assert.Equal(spots, network.Parking.Count);
        var points = network.Points;
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                Assert.True(Geo.DistanceMeters(points[i].Latitude, points[i].Longitude, points[j].Latitude, points[j].Longitude) >= 0.5, $"points {i} and {j} coincide");
            }
        }

        foreach (var path in network.Paths.Where(p => p.Type == MsfsCodes.ParkingPath))
        {
            var spot = network.Parking[path.End].Start;
            Assert.True(Geo.DistanceMeters(points[path.Start].Latitude, points[path.Start].Longitude, spot.Latitude, spot.Longitude) >= 5, "spur on its spot");
        }
    }

    [Fact]
    public void Ytwb_without_an_xplane_taxi_network_gets_a_generated_one()
    {
        var network = TaxiNetwork.Build(Fixtures.Airport("YTWB"));

        Assert.True(network.Generated);
        Assert.Equal(4, network.Points.Count(p => p.Type == MsfsCodes.HoldShortPoint));
        Assert.Equal(network.Parking.Count, network.Paths.Count(p => p.Type == MsfsCodes.ParkingPath));
        Assert.Equal(72, network.Parking.Count);
        Assert.Equal(network.Parking.Count, network.Parking.Select(p => (p.Name, p.Number, p.Suffix)).Distinct().Count());
        var starts = network.Paths.Where(p => p.Type == MsfsCodes.ParkingPath).Select(p => p.Start).ToList();
        Assert.Equal(starts.Count, starts.Distinct().Count());

        // Every hold-short point is off the runway and linked both to a runway end and to the apron.
        var ends = network.Paths.Where(p => p.Type == MsfsCodes.RunwayPath).SelectMany(p => new[] { p.Start, p.End }).ToHashSet();
        for (var i = 0; i < network.Points.Count; i++)
        {
            if (network.Points[i].Type != MsfsCodes.HoldShortPoint)
            {
                continue;
            }

            var links = network.Paths.Where(p => p.Type == MsfsCodes.TaxiwayPath && (p.Start == i || p.End == i)).ToList();
            Assert.Equal(2, links.Count);
            Assert.Contains(links, l => ends.Contains(l.Start == i ? l.End : l.Start));
        }
    }

    [Theory]
    [InlineData("YBBN")]
    [InlineData("YTWB")]
    public void No_two_taxi_points_share_a_position_and_no_spur_sits_on_its_spot(string icao)
    {
        // MSFS has neither (YBBN recording). The app's 1.8.21 crashed in TaxiPathCalculator.ConvertFlatToTaxiPoints on
        // X-Plane data with 90 coincident point pairs on parking spots (2026-10-01).
        var network = TaxiNetwork.Build(Fixtures.Airport(icao));
        var points = network.Points;
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                Assert.True(Geo.DistanceMeters(points[i].Latitude, points[i].Longitude, points[j].Latitude, points[j].Longitude) >= 0.5, $"points {i} and {j} coincide");
            }
        }

        var parkingPaths = network.Paths.Where(p => p.Type == MsfsCodes.ParkingPath).ToList();
        Assert.Equal(parkingPaths.Count, parkingPaths.Select(p => p.Start).Distinct().Count());
        var onSpot = parkingPaths.Count(p =>
            Geo.DistanceMeters(points[p.Start].Latitude, points[p.Start].Longitude, network.Parking[p.End].Start.Latitude, network.Parking[p.End].Start.Longitude) < 1);
        Assert.True(onSpot <= 1, $"{onSpot} spurs within 1 m of their spot");
    }
    [Fact]
    public void Frequency_names_are_callsigns_as_msfs_sends_them()
    {
        // MSFS YBBN: delivery and tower "BRISBANE", ground "North"/"South"/"Domestic", ATIS "YBBN".
        var airport = Fixtures.Airport("YBBN");
        var names = airport.Frequencies.ToDictionary(f => (f.Kind, f.Khz), f => MsfsCodes.FrequencyName(f, airport));

        Assert.All(names.Where(n => n.Key.Kind is FrequencyKind.Clearance or FrequencyKind.Tower or FrequencyKind.Approach or FrequencyKind.Departure), n => Assert.Equal("BRISBANE", n.Value));
        Assert.All(names.Where(n => n.Key.Kind == FrequencyKind.Atis), n => Assert.Equal("YBBN", n.Value));
        Assert.All(names.Where(n => n.Key.Kind == FrequencyKind.Ground), n => Assert.Equal("BRISBANE", n.Value)); // "BRISBANE GND" in the Gateway file
        Assert.Equal("North", MsfsCodes.FrequencyName(new Frequency(FrequencyKind.Ground, 124050, "SMC North"), airport)); // Orbx
        Assert.Equal("BRISBANE", MsfsCodes.FrequencyName(new Frequency(FrequencyKind.Clearance, 118600, "ACD"), airport));
    }
    [Fact]
    public void Automated_weather_is_type_12_as_msfs_sends_it()
    {
        // MSFS YTWB: 127.05 type 12 "YTWB", CTAF 127.65 type 4. X-Plane has "1050 127050 AWIS".
        var airport = Fixtures.Airport("YTWB");
        var types = airport.Frequencies.ToDictionary(f => f.Khz, MsfsCodes.Frequency);

        Assert.Equal(12, types[127050]);
        Assert.Equal(4, types[127650]);
        Assert.Equal("YTWB", MsfsCodes.FrequencyName(airport.Frequencies.Single(f => f.Khz == 127050), airport));
        Assert.Equal(1, MsfsCodes.Frequency(new Frequency(FrequencyKind.Atis, 125500, "ATIS")));
    }
    [Fact]
    public void Surrounding_airport_definition_encodes_without_taxi_data()
    {
        var root = AirportFacility.Build(Fixtures.Airport("YBBN"));
        var packets = new FacilityEncoder(Protocol).Encode(Fixtures.Definition(333), root, 5);
        var decoded = FacilityDecoder.Decode(Fixtures.Definition(333), packets);

        Assert.Empty(decoded.ChildrenOf("TAXI_POINT"));
        Assert.Equal(2, decoded.ChildrenOf("RUNWAY").Count);
    }

    private static FacilityObject Encoded(string icao)
    {
        var root = AirportFacility.Build(Fixtures.Airport(icao), new AirportExtras { MagneticVariationEast = 11 });
        var definition = Fixtures.Definition(444);
        var packets = new FacilityEncoder(Protocol).Encode(definition, root, 1);
        return FacilityDecoder.Decode(definition, packets);
    }

    private static double Num(FacilityObject o, string field) => Convert.ToDouble(o[field], System.Globalization.CultureInfo.InvariantCulture);

    private static bool Same(FacilityObject a, FacilityObject b, string field) => Num(a, field) == Num(b, field);

    private static double Metres(FacilityObject a, FacilityObject b) =>
        Geo.DistanceMeters(Num(a, "LATITUDE"), Num(a, "LONGITUDE"), Num(b, "LATITUDE"), Num(b, "LONGITUDE"));

    private static (double X, double Z) Bias(FacilityObject o) => (Num(o, "BIAS_X"), Num(o, "BIAS_Z"));

    private static double Distance((double X, double Z) a, (double X, double Z) b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Z - b.Z, 2));
}

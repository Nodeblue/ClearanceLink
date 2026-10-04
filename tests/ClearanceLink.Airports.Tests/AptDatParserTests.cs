using ClearanceLink.Airports;

namespace ClearanceLink.Airports.Tests;

public sealed class AptDatParserTests
{
    // A hand-written airport in apt.dat 1200 format (not X-Plane's data), covering each row type we read.
    private const string Sample = """
        I
        1200 Version - data cycle 2024.09
        
        1     13 0 0 YTST Test Field
        1302 city Testville
        1302 datum_lat -27.400000
        1302 datum_lon 153.100000
        1302 region_code YB
        1302 transition_alt 10000
        100 45.00 1 0 0.25 1 2 1 01 -27.41000000 153.10000000 120.00 60.00 3 5 1 1 19 -27.39000000 153.10500000 0.00 0.00 3 0 0 0
        1050 125500 ATIS
        1052 118850 CLEARANCE DELIVERY
        1053 122250 BRISBANE GROUND
        54 12070 BRISBANE TOWER
        1300 -27.40100000 153.09900000 297.00 gate jets|turboprops Gate 24
        1301 C airline QFA VOZ
        1300 -27.40200000 153.09800000 90.00 tie_down props GA 1
        1201 -27.40050000 153.09950000 both 0 A1
        1201 -27.40000000 153.10000000 junc 1
        1201 -27.39950000 153.10050000 both 2 Hold 01
        1202 0 1 twoway taxiway_E A
        1202 1 2 oneway taxiway A
        1204 departure 01,19
        1202 2 0 twoway runway 01/19
        99
        """;

    [Fact]
    public void A_row_with_a_word_where_a_number_belongs_is_skipped_not_fatal()
    {
        // Hand-edited custom scenery: one damaged runway row and one damaged gate row must not lose the airport.
        var text = Sample.Replace("1300 -27.40200000 153.09800000 90.00 tie_down props GA 1", "1300 -27.40200000 east 90.00 tie_down props GA 1\n100 wide 1 0 0.25 1 2 1 05 -27.41 153.10 0 0 3 5 1 1 23 -27.39 153.105 0 0 3 0 0 0", StringComparison.Ordinal);

        var airport = Assert.Single(AptDatParser.Parse(new StringReader(text)));

        Assert.Equal("YTST", airport.Icao);
        Assert.Single(airport.Runways);
        Assert.Single(airport.StartLocations);
        Assert.Equal(3, airport.TaxiNodes.Count);
    }

    [Fact]
    public void Reads_header_metadata_and_runways()
    {
        var airport = Assert.Single(AptDatParser.Parse(new StringReader(Sample)));

        Assert.Equal("YTST", airport.Icao);
        Assert.Equal("Test Field", airport.Name);
        Assert.Equal(13, airport.ElevationFeet);
        Assert.Equal("YB", airport.Region);
        Assert.Equal((-27.4, 153.1), airport.ReferencePoint);

        var runway = Assert.Single(airport.Runways);
        Assert.Equal("01/19", runway.Name);
        Assert.Equal(45, runway.WidthMeters);
        Assert.Equal(120, runway.End1.DisplacedThresholdMeters);
        Assert.Equal(5, runway.End1.ApproachLights);
        Assert.True(runway.End1.TouchdownZoneLights);
        Assert.InRange(runway.LengthMeters, 2250, 2300);
    }

    [Fact]
    public void Reads_frequencies_in_khz_including_legacy_rows()
    {
        var airport = AptDatParser.Find(new StringReader(Sample), "ytst")!;

        Assert.Equal(
            [
                new Frequency(FrequencyKind.Atis, 125500, "ATIS"),
                new Frequency(FrequencyKind.Clearance, 118850, "CLEARANCE DELIVERY"),
                new Frequency(FrequencyKind.Ground, 122250, "BRISBANE GROUND"),
                new Frequency(FrequencyKind.Tower, 120700, "BRISBANE TOWER"),
            ],
            airport.Frequencies);
    }

    [Fact]
    public void Reads_parking_with_airlines_and_taxi_network()
    {
        var airport = AptDatParser.Find(new StringReader(Sample), "YTST")!;

        Assert.Equal(2, airport.StartLocations.Count);
        var gate = airport.StartLocations[0];
        Assert.Equal("Gate 24", gate.Name);
        Assert.Equal(["jets", "turboprops"], gate.AircraftTypes);
        Assert.Equal("C", gate.SizeCategory);
        Assert.Equal(["QFA", "VOZ"], gate.Airlines);
        Assert.Null(airport.StartLocations[1].SizeCategory);

        Assert.Equal(3, airport.TaxiNodes.Count);
        Assert.Equal("Hold 01", airport.TaxiNodes[2].Name);
        Assert.Equal(3, airport.TaxiEdges.Count);
        Assert.Equal("E", airport.TaxiEdges[0].WidthClass);
        Assert.True(airport.TaxiEdges[1].OneWay);
        var zone = Assert.Single(airport.TaxiEdges[1].ActiveZones);
        Assert.Equal("departure", zone.Use);
        Assert.Equal(["01", "19"], zone.Runways);
        Assert.True(airport.TaxiEdges[2].IsRunway);
    }

    [Fact]
    public void Unknown_airport_is_null_and_junk_rows_are_ignored()
    {
        var text = Sample[..Sample.LastIndexOf("99", StringComparison.Ordinal)] + "garbage row\n1 5 0 0 YOTH Other\n99";

        Assert.Null(AptDatParser.Find(new StringReader(text), "NONE"));
        Assert.Equal(["YTST", "YOTH"], AptDatParser.Parse(new StringReader(text)).Select(a => a.Icao));
    }
}

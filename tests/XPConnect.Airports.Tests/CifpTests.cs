using XPConnect.Protocol.Facilities;

namespace XPConnect.Airports.Tests;

/// <summary>
/// CIFP parsing with hand-written lines in X-Plane's CIFP layout (real CIFP files are licensed and never committed).
/// The procedures, fixes and positions are invented.
/// </summary>
public sealed class CifpTests
{
    private static readonly string Sample = string.Join('\n',
        Line("SID", "010", "4", "TESTA1", "RW01L", "ALPHA", "YB", "P", "C", "E   ", "CF", theta: "0106", course: "0160"),
        Line("SID", "020", "4", "TESTA1", "RW01L", "BRAVO", "YB", "P", "C", "E   ", "TF", altDesc: "+", alt1: "06000"),
        Line("SID", "010", "4", "TESTA1", "RW19B", "", "", "", "", "    ", "CA", course: "1960", altDesc: "+", alt1: "00600"),
        Line("SID", "020", "4", "TESTA1", "RW19B", "BRAVO", "YB", "P", "C", "E   ", "DF"),
        Line("SID", "010", "5", "TESTA1", "ALL", "BRAVO", "YB", "P", "C", "E   ", "IF"),
        Line("SID", "020", "5", "TESTA1", "ALL", "CHARL", "YB", "E", "A", "E   ", "TF", altDesc: "B", alt1: "FL120", alt2: "08000", speedDesc: "-", speed: "250"),
        Line("SID", "010", "6", "TESTA1", "DELTA", "CHARL", "YB", "E", "A", "E   ", "IF"),
        Line("SID", "020", "6", "TESTA1", "DELTA", "DELTA", "YB", "D", " ", "E   ", "TF"),
        Line("STAR", "010", "1", "TESTB2", "ECHO", "ECHO", "YB", "E", "A", "E   ", "IF"),
        Line("STAR", "020", "1", "TESTB2", "ECHO", "FOXTR", "YB", "P", "C", "E   ", "TF"),
        Line("STAR", "010", "3", "TESTB2", "RW01L", "FOXTR", "YB", "P", "C", "E   ", "IF"),
        Line("STAR", "020", "3", "TESTB2", "RW01L", "GOLFA", "YB", "P", "C", "E   ", "TF", altDesc: "-", alt1: "05000"),
        Line("APPCH", "010", "A", "I01L", "HOTEL", "HOTEL", "YB", "P", "C", "E  A", "IF", altDesc: "+", alt1: "05000", speed: "210"),
        Line("APPCH", "020", "A", "I01L", "HOTEL", "INDIA", "YB", "P", "C", "E   ", "TF"),
        Line("APPCH", "030", "I", "I01L", "", "INDIA", "YB", "P", "C", "E  B", "IF", altDesc: "+", alt1: "04000"),
        Line("APPCH", "040", "I", "I01L", "", "JULIE", "YB", "P", "C", "E  F", "CF", theta: "1960", course: "0160", altDesc: "+", alt1: "02500"),
        Line("APPCH", "050", "I", "I01L", "", "RW01L", "YB", "P", "G", "GY M", "CF", course: "0160", alt1: "00066"),
        Line("APPCH", "060", "I", "I01L", "", "", "", "", "", "    ", "CA", course: "0160", altDesc: "+", alt1: "01000"),
        Line("APPCH", "070", "I", "I01L", "", "KILO", "YB", "P", "C", "E  H", "DF", altDesc: "+", alt1: "04000"),
        Line("APPCH", "010", "R", "R19RY", "", "LIMA", "YB", "P", "C", "E  F", "IF"),
        Line("APPCH", "020", "R", "R19RY", "", "RW19R", "YB", "P", "G", "GY M", "TF"),
        "RWY:RW01L,     ,      ,00013, ,ITS ,1,   ;S27225905,E153062449,0000;",
        "RWY:RW19L,     ,      ,00013, ,    , ,   ;S27213000,E153071000,0000;",
        "RWY:RW19R,     ,      ,00013, ,    , ,   ;S27214000,E153072000,0000;",
        "PRDAT:not a procedure line;",
        "APPCH:broken");

    // One CIFP procedure line with the ARINC fields at X-Plane's positions (38 comma-separated fields).
    private static string Line(
        string kind, string seq, string routeType, string ident, string transition, string fix, string region, string section,
        string subsection, string description, string pathTerminator, string theta = "    ", string course = "    ",
        string altDesc = " ", string alt1 = "     ", string alt2 = "     ", string speedDesc = " ", string speed = "   ")
    {
        var f = Enumerable.Repeat(" ", 38).ToArray();
        (f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8]) = (seq, routeType, ident, transition, fix, region, section, subsection, description);
        (f[10], f[11], f[18], f[20], f[22], f[23], f[24], f[25], f[26], f[27]) = ("010", pathTerminator, theta, course, altDesc, alt1, alt2, "10000", speedDesc, speed);
        return $"{kind}:{string.Join(',', f)};";
    }

    private static readonly NavData Nav = NavData.Load(
        new StringReader("""
            I
            1200 Version - data cycle 2409
            -27.100000000  153.200000000 ALPHA YBBN YB 4530263
            -27.000000000  153.300000000 BRAVO YBBN YB 4530263
            -26.500000000  153.400000000 CHARL ENRT YB 2105430
            40.000000000  -70.000000000 CHARL ENRT K6 2105430
            -27.600000000  153.000000000 INDIA YBBN YB 4530263
            99
            """),
        new StringReader("""
            I
            1200 Version - data cycle 2409
            3  -27.300000000  153.100000000     20 11630 130    11.000 DELTA ENRT YB DELTA VOR/DME
            3  -33.900000000  151.200000000     20 11390 130    12.500 FAR ENRT YM FAR AWAY VOR
            99
            """));

    private static (List<FacilityObject> Approaches, List<FacilityObject> Departures, List<FacilityObject> Arrivals) Build() =>
        Cifp.Parse(new StringReader(Sample)).ToFacilities("YBBN", -27.38, 153.11, Nav);

    [Fact]
    public void Arinc_coordinates_are_degrees_minutes_seconds()
    {
        Assert.True(Cifp.TryArincLatitude("S27225905", out var lat));
        Assert.True(Cifp.TryArincLatitude("E153062449", out var lon));
        Assert.Equal(-(27 + (22 / 60.0) + (59.05 / 3600)), lat, 9);
        Assert.Equal(153 + (6 / 60.0) + (24.49 / 3600), lon, 9);
    }

    [Theory]
    [InlineData("IF", 15)]
    [InlineData("CF", 4)]
    [InlineData("TF", 18)]
    [InlineData("CA", 2)]
    [InlineData("DF", 7)]
    [InlineData("RF", 17)]
    [InlineData("VM", 22)]
    public void Path_terminators_use_msfs_numbering(string code, int msfs) => Assert.Equal(msfs, Cifp.PathTerminator(code));

    [Theory]
    [InlineData("I01L", 4, "01L", '0')]
    [InlineData("R19RY", 10, "19R", 'Y')]
    [InlineData("R01-Z", 10, "01", 'Z')]
    [InlineData("L28", 5, "28", '0')]
    [InlineData("VDM-A", 2, "", 'A')]
    public void Approach_idents_split_into_type_runway_and_suffix(string ident, int type, string runway, char suffix) =>
        Assert.Equal((type, runway, (int)suffix), Cifp.ApproachIdent(ident));

    [Fact]
    public void Runway_lines_give_thresholds_and_ils()
    {
        var cifp = Cifp.Parse(new StringReader(Sample));

        Assert.Equal(3, cifp.Runways.Count);
        Assert.Equal("ITS", cifp.Ils()["01L"]);
        Assert.Single(cifp.Ils());
    }

    [Fact]
    public void Departure_has_runway_common_and_enroute_parts()
    {
        var departure = Assert.Single(Build().Departures);

        Assert.Equal("TESTA1", departure["NAME"]);
        var runways = departure.ChildrenOf("RUNWAY_TRANSITION");
        // RW01L, then RW19B expanded to both parallel 19s in the file.
        Assert.Equal([(1, 1), (19, 1), (19, 2)], runways.Select(r => ((int)r["RUNWAY_NUMBER"]!, (int)r["RUNWAY_DESIGNATOR"]!)));
        Assert.Equal(3, departure["N_RUNWAY_TRANSITIONS"]);
        Assert.Equal(2, departure["N_APPROACH_LEGS"]);
        Assert.Equal("DELTA", Assert.Single(departure.ChildrenOf("ENROUTE_TRANSITION"))["NAME"]);

        var first = runways[0].ChildrenOf("APPROACH_LEG")[0];
        Assert.Equal(4, first["TYPE"]);
        Assert.Equal("ALPHA", first["FIX_ICAO"]);
        Assert.Equal((int)'W', first["FIX_TYPE"]);
        Assert.Equal(16.0, first["COURSE"]);
        Assert.Equal(10.6, first["THETA"]);
        Assert.Equal(-27.1, first["FIX_LATITUDE"]);
        Assert.Equal(-1.0, first["SPEED_LIMIT"]);
        Assert.Equal(0, first["APPROACH_ALT_DESC"]);

        var climb = runways[0].ChildrenOf("APPROACH_LEG")[1];
        Assert.Equal(2, climb["APPROACH_ALT_DESC"]);
        Assert.Equal(6000 * 0.3048, (double)climb["ALTITUDE1"]!, 6);

        // Between FL120 and 8,000 ft with a 250 kt limit; the region picks the Australian CHARL, not the American one.
        var between = departure.ChildrenOf("APPROACH_LEG")[1];
        Assert.Equal(4, between["APPROACH_ALT_DESC"]);
        Assert.Equal(12000 * 0.3048, (double)between["ALTITUDE1"]!, 6);
        Assert.Equal(8000 * 0.3048, (double)between["ALTITUDE2"]!, 6);
        Assert.Equal(250.0, between["SPEED_LIMIT"]);
        Assert.Equal(3, between["APPROACH_SPEED_DESC"]);
        Assert.Equal(-26.5, between["FIX_LATITUDE"]);

        var vor = departure.ChildrenOf("ENROUTE_TRANSITION")[0].ChildrenOf("APPROACH_LEG")[1];
        Assert.Equal((int)'V', vor["FIX_TYPE"]);
        Assert.Equal(-27.3, vor["FIX_LATITUDE"]);
    }

    [Fact]
    public void Arrival_has_enroute_and_runway_transitions()
    {
        var arrival = Assert.Single(Build().Arrivals);

        Assert.Equal("ECHO", Assert.Single(arrival.ChildrenOf("ENROUTE_TRANSITION"))["NAME"]);
        var runway = Assert.Single(arrival.ChildrenOf("RUNWAY_TRANSITION"));
        Assert.Equal(1, runway["RUNWAY_NUMBER"]);
        Assert.Equal(3, runway.ChildrenOf("APPROACH_LEG")[1]["APPROACH_ALT_DESC"]);
    }

    [Fact]
    public void Approach_splits_at_the_missed_approach_point()
    {
        var approaches = Build().Approaches;
        var ils = approaches.Single(a => (int)a["TYPE"]! == 4);

        Assert.Equal((int)'0', ils["SUFFIX"]);
        Assert.Equal(1, ils["RUNWAY_NUMBER"]);
        Assert.Equal(1, ils["RUNWAY_DESIGNATOR"]);
        var transition = Assert.Single(ils.ChildrenOf("APPROACH_TRANSITION"));
        Assert.Equal("HOTEL", transition["NAME"]);
        Assert.Equal(1, transition.ChildrenOf("APPROACH_LEG")[0]["IS_IAF"]);

        var final = ils.ChildrenOf("FINAL_APPROACH_LEG");
        Assert.Equal(["INDIA", "JULIE", "RW01L"], final.Select(l => (string)l["FIX_ICAO"]!));
        Assert.Equal([1, 0, 0], final.Select(l => (int)l["IS_IF"]!));
        Assert.Equal([0, 1, 0], final.Select(l => (int)l["IS_FAF"]!));
        Assert.Equal([0, 0, 1], final.Select(l => (int)l["IS_MAP"]!));
        Assert.Equal((int)'R', final[2]["FIX_TYPE"]);
        Assert.Equal(1, final[2]["APPROACH_ALT_DESC"]);
        Assert.Equal(-(27 + (22 / 60.0) + (59.05 / 3600)), (double)final[2]["FIX_LATITUDE"]!, 9);
        Assert.Equal("JULIE", ils["FAF_ICAO"]);

        var missed = ils.ChildrenOf("MISSED_APPROACH_LEG");
        Assert.Equal([2, 7], missed.Select(l => (int)l["TYPE"]!));
        Assert.Equal(3, ils["N_FINAL_APPROACH_LEGS"]);
        Assert.Equal(2, ils["N_MISSED_APPROACH_LEGS"]);

        var rnav = approaches.Single(a => (int)a["TYPE"]! == 10);
        Assert.Equal((int)'Y', rnav["SUFFIX"]);
        Assert.Equal(19, rnav["RUNWAY_NUMBER"]);
        Assert.Equal(2, rnav["RUNWAY_DESIGNATOR"]);
    }

    [Fact]
    public void Magnetic_variation_comes_from_the_nearest_vor()
    {
        Assert.Equal(11.0, Nav.Variation(-27.38, 153.11));
        Assert.Null(Nav.Variation(10, 10));
    }

    [Fact]
    public void Airport_magvar_is_sent_west_positive_like_msfs()
    {
        var root = AirportFacility.Build(Fixtures.Airport("YTWB"), new AirportExtras { MagneticVariationEast = 11 });

        Assert.Equal(349.0, root["MAGVAR"]);
    }
}

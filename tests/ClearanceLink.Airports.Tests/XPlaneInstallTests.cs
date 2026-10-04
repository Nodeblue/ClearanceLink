namespace ClearanceLink.Airports.Tests;

/// <summary>Builds a small fake X-Plane folder and reads airports from it the way serve does.</summary>
public sealed class XPlaneInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clearancelink-xp-" + Guid.NewGuid().ToString("N"));

    public XPlaneInstallTests()
    {
        // Global Airports has YBBN and YTWB (the committed excerpts); a custom pack overrides YTWB with a renamed copy,
        // and a disabled pack that would override YBBN must be ignored.
        Write("Global Scenery/Global Airports/Earth nav data/apt.dat", Excerpt("YBBN") + Excerpt("YTWB") + "99\n");
        Write("Custom Scenery/Custom YTWB/Earth nav data/apt.dat", Excerpt("YTWB").Replace("Toowoomba", "Custom Toowoomba", StringComparison.Ordinal) + "99\n");
        Write("Custom Scenery/Disabled YBBN/Earth nav data/apt.dat", Excerpt("YBBN").Replace("Brisbane", "Wrong Brisbane", StringComparison.Ordinal) + "99\n");
        Write("Custom Scenery/scenery_packs.ini", """
            I
            1000 Version
            SCENERY

            SCENERY_PACK Custom Scenery/Custom YTWB/
            SCENERY_PACK_DISABLED Custom Scenery/Disabled YBBN/
            SCENERY_PACK *GLOBAL_AIRPORTS*
            """);
        Write("Resources/default data/earth_fix.dat", "I\n1200 Version\n-27.1 153.2 ALPHA YBBN YB 4530263\n99\n");
        Write("Resources/default data/earth_nav.dat", "I\n1200 Version\n3 -27.3 153.1 20 11630 130 11.000 DELTA ENRT YB DELTA VOR/DME\n99\n");
        Write("Custom Data/CIFP/YBBN.dat", """
            SID:010,4,TESTA1,RW01L,ALPHA,YB,P,C,E   , ,010,CF, , , , , ,      ,0106,    ,0160,    , ,     ,     ,10000, ,   , , , , , , , , , , ;
            RWY:RW01L,     ,      ,00013, ,ITS ,1,   ;S27225905,E153062449,0000;
            """);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Airport_files_follow_scenery_packs_order()
    {
        var files = new XPlaneInstall(_root).AirportFiles();

        Assert.Equal(2, files.Count);
        Assert.Contains("Custom YTWB", files[0], StringComparison.Ordinal);
        Assert.Contains("Global Airports", files[1], StringComparison.Ordinal);
    }

    [Fact]
    public void First_pack_defining_an_airport_wins()
    {
        var index = AptDatIndex.Build(new XPlaneInstall(_root).AirportFiles());

        Assert.StartsWith("Custom Toowoomba", index.Load("YTWB")!.Name, StringComparison.Ordinal);
        Assert.StartsWith("Brisbane", index.Load("YBBN")!.Name, StringComparison.Ordinal);
        Assert.Equal(2, index.Load("YBBN")!.Runways.Count);
        Assert.Null(index.Load("YSSY"));
    }

    [Fact]
    public void Serve_builds_airports_with_procedures_and_magvar()
    {
        var log = new List<string>();
        var airports = new XPlaneAirports(new XPlaneInstall(_root), log.Add);

        var ybbn = airports.Find("YBBN")!;
        Assert.Single(ybbn.ChildrenOf("DEPARTURE"));
        Assert.Equal(349.0, ybbn["MAGVAR"]);
        Assert.Equal("ITS", ybbn.ChildrenOf("RUNWAY").Select(r => r["SECONDARY_ILS_ICAO"]).Single(i => i is not null));
        Assert.Same(ybbn, airports.Find("ybbn"));

        var ytwb = airports.Find("YTWB")!;
        Assert.Empty(ytwb.ChildrenOf("APPROACH"));
        Assert.Contains(log, l => l.Contains("YTWB", StringComparison.Ordinal) && l.Contains("taxi routes generated", StringComparison.Ordinal));
        Assert.Null(airports.Find("YSSY"));
    }

    [Fact]
    public void Frequency_kinds_missing_from_custom_scenery_come_from_global_airports()
    {
        // The custom YTWB keeps only its first frequency row; Global Airports' YTWB has the rest.
        var custom = Path.Combine(_root, "Custom Scenery", "Custom YTWB", "Earth nav data", "apt.dat");
        var lines = File.ReadAllLines(custom).ToList();
        var frequencies = lines.Where(l => l.StartsWith("105", StringComparison.Ordinal)).ToList();
        Assert.True(frequencies.Count >= 2);
        File.WriteAllLines(custom, lines.Where(l => l == frequencies[0] || !frequencies.Contains(l)));

        var index = AptDatIndex.Build(new XPlaneInstall(_root).AirportFiles());
        var airport = index.Load("YTWB")!;
        Assert.Single(airport.Frequencies);
        var note = XPlaneAirports.BorrowFrequencies(airport, index.LoadOthers("YTWB"));

        // YTWB lists CTAF and ATIS: the custom copy keeps CTAF, ATIS comes from Global Airports, nothing is doubled.
        Assert.Equal([FrequencyKind.Atis, FrequencyKind.Unicom], airport.Frequencies.Select(f => f.Kind).Order());
        Assert.Contains("lower-priority", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Scenery_sequence_reads_enabled_packs_only()
    {
        var packs = XPlaneInstall.ScenerySequence(["SCENERY_PACK Custom Scenery/A/", "SCENERY_PACK_DISABLED Custom Scenery/B/", "SCENERY_PACK *GLOBAL_AIRPORTS*"]).ToList();

        Assert.Equal(2, packs.Count);
        Assert.Equal("*GLOBAL_AIRPORTS*", packs[1]);
    }

    private static string Excerpt(string icao)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "tests", "ClearanceLink.Airports.Tests", "fixtures", $"{icao}.apt.dat");
            if (File.Exists(path))
            {
                // The excerpt without the file header lines or the closing 99, so several can be joined.
                return string.Join('\n', File.ReadAllLines(path).Where(l => l.Trim() is not ("I" or "A" or "99") && !l.StartsWith("1200 ", StringComparison.Ordinal) && !l.StartsWith("1100 ", StringComparison.Ordinal))) + "\n";
            }
        }

        throw new FileNotFoundException(icao);
    }

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "I\n1200 Version\n\n" + text.Replace("\r\n", "\n", StringComparison.Ordinal));
        return path;
    }
}

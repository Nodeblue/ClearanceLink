namespace ClearanceLink.Airports.Tests;

public sealed class AptDatIndexCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("clearancelink-aptindex").FullName;
    private readonly List<string> _log = [];

    private string Cache => Path.Combine(_dir, "cache", "apt-index.json");

    [Fact]
    public void The_cache_round_trips_and_gives_the_same_airports()
    {
        var files = Files();

        var built = AptDatIndex.Build(files, Cache, _log.Add);
        Assert.False(built.FromCache);
        Assert.True(File.Exists(Cache));

        var cached = AptDatIndex.Build(files, Cache, _log.Add);
        Assert.True(cached.FromCache);
        Assert.Equal(built.Count, cached.Count);
        foreach (var icao in new[] { "YAAA", "YBBB", "YCCC", "XBBB" })
        {
            Assert.True(built.TryGet(icao, out var file, out var offset));
            Assert.True(cached.TryGet(icao, out var cachedFile, out var cachedOffset));
            Assert.Equal((file, offset), (cachedFile, cachedOffset));
        }

        Assert.Equal("Bravo custom", cached.Load("YBBB")!.Name);
        Assert.Equal(["Bravo global"], cached.LoadOthers("YBBB").Select(a => a.Name));
        Assert.Empty(_log);
    }

    [Fact]
    public void A_changed_file_or_file_list_rebuilds_the_cache()
    {
        var files = Files();
        AptDatIndex.Build(files, Cache);

        File.AppendAllText(files[1], "1 10 0 0 YDDD Delta\n99\n");
        var changed = AptDatIndex.Build(files, Cache);
        Assert.False(changed.FromCache);
        Assert.True(changed.TryGet("YDDD", out _, out _));
        Assert.True(AptDatIndex.Build(files, Cache).FromCache);

        File.SetLastWriteTimeUtc(files[0], File.GetLastWriteTimeUtc(files[0]).AddMinutes(-5)); // same size, new time
        Assert.False(AptDatIndex.Build(files, Cache).FromCache);

        var fewer = AptDatIndex.Build(files.Take(1).ToList(), Cache);
        Assert.False(fewer.FromCache);
        Assert.False(fewer.TryGet("YCCC", out _, out _));

        var reordered = AptDatIndex.Build([files[1], files[0]], Cache);
        Assert.False(reordered.FromCache);
        Assert.Equal("Bravo global", reordered.Load("YBBB")!.Name);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"Format\":1,\"Files\":null}")]
    [InlineData("{\"Format\":99,\"Files\":[],\"Airports\":{}}")]
    public void A_damaged_cache_is_ignored(string content)
    {
        var files = Files();
        Directory.CreateDirectory(Path.GetDirectoryName(Cache)!);
        File.WriteAllText(Cache, content);

        var index = AptDatIndex.Build(files, Cache, _log.Add);

        Assert.False(index.FromCache);
        Assert.True(index.TryGet("YAAA", out _, out _));
        Assert.True(AptDatIndex.Build(files, Cache).FromCache); // rewritten
    }

    [Fact]
    public void A_cache_entry_pointing_at_no_file_is_ignored()
    {
        var files = Files();
        AptDatIndex.Build(files, Cache);
        File.WriteAllText(Cache, File.ReadAllText(Cache).Replace("[0,", "[7,", StringComparison.Ordinal));

        var index = AptDatIndex.Build(files, Cache, _log.Add);

        Assert.False(index.FromCache);
        Assert.Single(_log, l => l.StartsWith("airports: ignoring the apt.dat index cache", StringComparison.Ordinal));
    }

    [Fact]
    public void A_cache_that_cannot_be_written_is_logged_and_the_index_still_works()
    {
        var files = Files();
        Directory.CreateDirectory(Cache); // a folder where the file should go

        var index = AptDatIndex.Build(files, Cache, _log.Add);

        Assert.True(index.TryGet("YAAA", out _, out _));
        Assert.Single(_log, l => l.StartsWith("airports: could not write the apt.dat index cache", StringComparison.Ordinal));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private List<string> Files()
    {
        var custom = Path.Combine(_dir, "custom.apt.dat");
        var global = Path.Combine(_dir, "global.apt.dat");
        File.WriteAllText(custom, "I\n1200 Version\n1 10 0 0 YAAA Alpha\n1 10 0 0 YBBB Bravo custom\n1302 icao_code XBBB\n99\n");
        File.WriteAllText(global, "I\n1200 Version\n1 10 0 0 YBBB Bravo global\n1 10 0 0 YCCC Charlie\n");
        return [custom, global];
    }
}

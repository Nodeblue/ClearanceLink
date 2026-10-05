using System.Text;
using System.Text.Json;

namespace ClearanceLink.Airports;

/// <summary>
/// Where each airport starts in a set of apt.dat files, so one airport can be read without parsing the 360 MB
/// Global Airports file. Files are given in priority order; the first file defining an ICAO wins, as in X-Plane.
/// Airports are found by their header id and by their 1302 icao_code. The index can be cached in a JSON file keyed by
/// each apt.dat's path, size and last write time, so a start with unchanged scenery skips the scan.
/// </summary>
public sealed class AptDatIndex
{
    private readonly Dictionary<string, (string File, long Offset)> _airports = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<(string File, long Offset)>> _lower = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bumped whenever <see cref="Scan"/> indexes differently, so older caches are rebuilt.</summary>
    private const int CacheFormat = 1;

    public int Count => _airports.Count;

    /// <summary>True when this index was read from the cache file rather than built by scanning.</summary>
    public bool FromCache { get; private set; }

    public static AptDatIndex Build(IEnumerable<string> files)
    {
        var index = new AptDatIndex();
        foreach (var file in files)
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            index.Scan(file, stream);
        }

        return index;
    }

    /// <summary>
    /// The index from <paramref name="cacheFile"/> when it was made from exactly these files (same order, paths, sizes
    /// and last write times); otherwise a fresh scan, which is then written to the cache. A damaged or unreadable cache
    /// is ignored and a cache that cannot be written is only logged.
    /// </summary>
    public static AptDatIndex Build(IReadOnlyList<string> files, string? cacheFile, Action<string>? log = null)
    {
        if (cacheFile is null)
        {
            return Build(files);
        }

        var stamps = files.Select(Stamp).ToList();
        if (ReadCache(cacheFile, stamps, log) is { } cached)
        {
            return cached;
        }

        var index = Build(files);
        index.WriteCache(cacheFile, stamps, log);
        return index;
    }

    private static FileStamp Stamp(string file)
    {
        var info = new FileInfo(file);
        return new FileStamp(file, info.Length, info.LastWriteTimeUtc.Ticks);
    }

    private static AptDatIndex? ReadCache(string cacheFile, List<FileStamp> stamps, Action<string>? log)
    {
        try
        {
            if (!File.Exists(cacheFile))
            {
                return null;
            }

            var cache = JsonSerializer.Deserialize<IndexCache>(File.ReadAllBytes(cacheFile));
            if (cache is null || cache.Format != CacheFormat || cache.Files is null || cache.Airports is null || !cache.Files.SequenceEqual(stamps))
            {
                return null;
            }

            var index = new AptDatIndex { FromCache = true };
            foreach (var (icao, at) in cache.Airports)
            {
                index._airports[icao] = Entry(cache.Files, at);
            }

            foreach (var (icao, list) in cache.Lower ?? [])
            {
                index._lower[icao] = [.. list.Select(at => Entry(cache.Files, at))];
            }

            return index;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            log?.Invoke($"airports: ignoring the apt.dat index cache {cacheFile} ({ex.Message}); scanning again");
            return null;
        }
    }

    private static (string File, long Offset) Entry(List<FileStamp> files, long[]? at) =>
        at is [var file, var offset] && file >= 0 && file < files.Count && offset >= 0
            ? (files[(int)file].Path, offset)
            : throw new InvalidDataException("bad airport entry");

    private void WriteCache(string cacheFile, List<FileStamp> stamps, Action<string>? log)
    {
        var position = new Dictionary<string, long>(StringComparer.Ordinal);
        for (var i = 0; i < stamps.Count; i++)
        {
            position.TryAdd(stamps[i].Path, i);
        }

        long[] At((string File, long Offset) entry) => [position[entry.File], entry.Offset];
        var cache = new IndexCache
        {
            Format = CacheFormat,
            Files = stamps,
            Airports = _airports.ToDictionary(e => e.Key, e => At(e.Value), StringComparer.OrdinalIgnoreCase),
            Lower = _lower.ToDictionary(e => e.Key, e => e.Value.Select(At).ToList(), StringComparer.OrdinalIgnoreCase),
        };
        var temp = cacheFile + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cacheFile))!);
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(cache));
            File.Move(temp, cacheFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"airports: could not write the apt.dat index cache {cacheFile} ({ex.Message}); the next start scans again");
        }
    }

    public bool TryGet(string icao, out string file, out long offset)
    {
        var found = _airports.TryGetValue(icao, out var entry);
        (file, offset) = entry;
        return found;
    }

    public Airport? Load(string icao) => TryGet(icao, out var file, out var offset) ? LoadAt(file, offset) : null;

    /// <summary>The same airport as defined by lower-priority files (for example Global Airports under a custom scenery pack).</summary>
    public IEnumerable<Airport> LoadOthers(string icao)
    {
        foreach (var (file, offset) in _lower.GetValueOrDefault(icao) ?? [])
        {
            if (LoadAt(file, offset) is { } airport)
            {
                yield return airport;
            }
        }
    }

    private static Airport? LoadAt(string file, long offset)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return AptDatParser.Parse(reader).FirstOrDefault();
    }

    /// <summary>Indexes one file's airports. Public for tests, which index a stream.</summary>
    public void Scan(string file, Stream stream)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long headerOffset = -1;
        foreach (var (offset, line) in Lines(stream))
        {
            if (line.Length < 2)
            {
                continue;
            }

            var code = FirstWord(line);
            if (code is "1" or "16" or "17")
            {
                headerOffset = offset;
                var words = line.Split((char[]?)null, 6, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length >= 5)
                {
                    Add(words[4], file, offset, seen);
                }
            }
            else if (code == "1302" && headerOffset >= 0 && line.Contains(" icao_code ", StringComparison.Ordinal))
            {
                var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length >= 3)
                {
                    Add(words[2], file, headerOffset, seen);
                }
            }
        }
    }

    private void Add(string icao, string file, long offset, HashSet<string> seen)
    {
        // Earlier files win; within a file the first definition wins.
        if (seen.Add(icao) && !_airports.TryAdd(icao, (file, offset)))
        {
            if (!_lower.TryGetValue(icao, out var list))
            {
                _lower[icao] = list = [];
            }

            list.Add((file, offset));
        }
    }

    private static string FirstWord(string line)
    {
        var end = line.IndexOf(' ', StringComparison.Ordinal);
        return end < 0 ? line.Trim() : line[..end];
    }

    /// <summary>Lines with their byte offsets. Only lines starting with 1, 16, 17 or 1302 are decoded.</summary>
    private static IEnumerable<(long Offset, string Line)> Lines(Stream stream)
    {
        var buffer = new byte[1 << 20];
        var carry = 0;
        long bufferStart = 0;
        int read;
        while ((read = stream.Read(buffer, carry, buffer.Length - carry)) > 0)
        {
            var filled = carry + read;
            var lineStart = 0;
            int newline;
            while ((newline = buffer.AsSpan(lineStart, filled - lineStart).IndexOf((byte)'\n')) >= 0)
            {
                var line = buffer.AsSpan(lineStart, newline);
                if (Interesting(line))
                {
                    yield return (bufferStart + lineStart, Decode(buffer, lineStart, newline));
                }

                lineStart += newline + 1;
            }

            // Keep the unfinished line for the next read (a line longer than the buffer is dropped).
            carry = filled - lineStart;
            if (carry == buffer.Length)
            {
                carry = 0;
            }

            buffer.AsSpan(lineStart, carry).CopyTo(buffer);
            bufferStart += lineStart == 0 && carry == 0 ? filled : lineStart;
        }

        if (carry > 0 && Interesting(buffer.AsSpan(0, carry)))
        {
            yield return (bufferStart, Decode(buffer, 0, carry));
        }
    }

    private static string Decode(byte[] buffer, int start, int length) => Encoding.UTF8.GetString(buffer, start, length).TrimEnd('\r');

    private static bool Interesting(ReadOnlySpan<byte> line) =>
        line.StartsWith("1 "u8) || line.StartsWith("1\t"u8) || line.StartsWith("16 "u8) || line.StartsWith("17 "u8) || line.StartsWith("1302 "u8);
}

internal sealed record FileStamp(string Path, long Size, long LastWriteTicks);

internal sealed class IndexCache
{
    public int Format { get; set; }

    public List<FileStamp>? Files { get; set; }

    /// <summary>ICAO to [position in Files, byte offset].</summary>
    public Dictionary<string, long[]>? Airports { get; set; }

    public Dictionary<string, List<long[]>>? Lower { get; set; }
}

using System.Text;

namespace XPConnect.Airports;

/// <summary>
/// Where each airport starts in a set of apt.dat files, so one airport can be read without parsing the 360 MB
/// Global Airports file. Files are given in priority order; the first file defining an ICAO wins, as in X-Plane.
/// Airports are found by their header id and by their 1302 icao_code.
/// </summary>
public sealed class AptDatIndex
{
    private readonly Dictionary<string, (string File, long Offset)> _airports = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<(string File, long Offset)>> _lower = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _airports.Count;

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

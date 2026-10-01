using System.Text.Json;
using XPConnect.Protocol;

namespace XPConnect.Recorder;

/// <summary>Appends every packet to a .jsonl file, one JSON object per line, flushed immediately so a crash loses nothing.</summary>
public sealed class JsonlRecorder : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly Lock _lock = new();
    private readonly TimeProvider _time;

    public JsonlRecorder(string path, TimeProvider? time = null, RecordThrottle? throttle = null)
    {
        Throttle = throttle ?? RecordThrottle.None;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        Path = path;
        _writer = new StreamWriter(File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        _time = time ?? TimeProvider.System;
    }

    public string Path { get; }

    public RecordThrottle Throttle { get; }

    public event Action<RecordEntry>? Recorded;

    /// <summary>Records the packet, or returns null when the throttle samples it out.</summary>
    public RecordEntry? Record(Packet packet, int connection)
    {
        if (!Throttle.ShouldRecord(packet, connection))
        {
            return null;
        }

        var entry = RecordEntry.FromPacket(packet, connection, _time.GetUtcNow());
        var line = JsonSerializer.Serialize(entry);
        lock (_lock)
        {
            _writer.WriteLine(line);
        }

        Recorded?.Invoke(entry);
        return entry;
    }

    public static IEnumerable<RecordEntry> ReadAll(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            yield return JsonSerializer.Deserialize<RecordEntry>(line)
                ?? throw new InvalidDataException($"Bad recording line in {path}");
        }
    }

    public void Dispose() => _writer.Dispose();
}

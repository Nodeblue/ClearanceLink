using System.Text.Json;
using ClearanceLink.Recorder;

namespace ClearanceLink.Host;

/// <summary>Prints one readable line per recorded packet.</summary>
public static class PacketConsole
{
    public const int RecentLines = 1000;

    private static readonly Lock Gate = new();
    private static readonly Queue<string> Recent = new();

    /// <summary>Raised for every status line (the panel's live log listens).</summary>
    public static event Action<string>? Line;

    public static bool Verbose { get; set; } = true;

    public static void Print(RecordEntry? e)
    {
        if (!Verbose || e is null)
        {
            return;
        }

        var fields = string.Join(' ', e.Fields
            .Where(kv => !kv.Key.EndsWith("Hex", StringComparison.Ordinal))
            .Select(kv => $"{kv.Key}={Format(kv.Value)}"));
        var arrow = e.Dir == "c2s" ? "->" : "<-";
        lock (Gate)
        {
            Console.WriteLine($"{e.Time.ToLocalTime():HH:mm:ss.fff} #{e.Connection} {arrow} {e.Name} {fields}");
        }
    }

    /// <summary>A status line: printed, and copied to the session log file when one is open.</summary>
    public static void Info(string message)
    {
        var now = DateTimeOffset.Now;
        var line = $"{now:HH:mm:ss.fff} ** {message}";
        lock (Gate)
        {
            Console.WriteLine(line);
            Recent.Enqueue(line);
            if (Recent.Count > RecentLines)
            {
                Recent.Dequeue();
            }
        }

        Support.SessionLog.Write($"{now:yyyy-MM-dd HH:mm:ss.fff} ** {message}");
        Line?.Invoke(line);
    }

    /// <summary>The last <see cref="RecentLines"/> status lines, oldest first.</summary>
    public static IReadOnlyList<string> RecentStatusLines()
    {
        lock (Gate)
        {
            return [.. Recent];
        }
    }

    private static string Format(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? $"\"{value.GetString()}\"" : value.ToString();
}

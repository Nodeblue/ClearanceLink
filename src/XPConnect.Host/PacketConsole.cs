using System.Text.Json;
using XPConnect.Recorder;

namespace XPConnect.Host;

/// <summary>Prints one readable line per recorded packet.</summary>
public static class PacketConsole
{
    private static readonly Lock Gate = new();

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
        lock (Gate)
        {
            Console.WriteLine($"{now:HH:mm:ss.fff} ** {message}");
        }

        Support.SessionLog.Write($"{now:yyyy-MM-dd HH:mm:ss.fff} ** {message}");
    }

    private static string Format(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? $"\"{value.GetString()}\"" : value.ToString();
}

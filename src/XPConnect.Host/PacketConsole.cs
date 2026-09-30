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

    public static void Info(string message)
    {
        lock (Gate)
        {
            Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} ** {message}");
        }
    }

    private static string Format(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? $"\"{value.GetString()}\"" : value.ToString();
}

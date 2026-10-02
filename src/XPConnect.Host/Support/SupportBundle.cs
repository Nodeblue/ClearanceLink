using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace XPConnect.Host.Support;

/// <summary>Where the files for a support zip come from. Null folders are skipped.</summary>
public sealed record SupportSources(
    string AppVersion,
    AppFolders Folders,
    string? BatcFolder,
    string? XPlaneFolder,
    string? BatcLogFolder,
    Redactor Redactor)
{
    /// <summary>BeyondATC's Player.log folder for the current Windows user.</summary>
    public static string DefaultBatcLogFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Skirmish Mode Games, Inc", "BeyondATC");
}

/// <summary>
/// Builds one zip a user can send for troubleshooting: about.txt (versions, folders, BATC redirect state), settings,
/// the newest session logs and recording, BeyondATC's Player.log and the end of X-Plane's Log.txt. Every text file
/// goes through the <see cref="Redactor"/> first.
/// </summary>
public static class SupportBundle
{
    public const int LogsIncluded = 5;
    public const long TailBytes = 5 * 1024 * 1024;
    public const long MaxRecordingBytes = 300L * 1024 * 1024;

    private static readonly string[] WatchedProcesses = ["FlightSimulator2024", "FlightSimulator", "BeyondATC", "simconnect_ws", "X-Plane"];

    public static string Create(SupportSources src, DateTime now)
    {
        Directory.CreateDirectory(src.Folders.SupportFiles);
        var zipPath = Path.Combine(src.Folders.SupportFiles, $"XPConnect-support-{now:yyyyMMdd-HHmmss}.zip");
        var notes = new List<string>();
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            if (File.Exists(src.Folders.Settings))
            {
                AddText(zip, "settings.json", src.Folders.Settings, src, notes);
            }

            foreach (var log in Newest(src.Folders.Logs, "*.log", LogsIncluded))
            {
                AddText(zip, $"logs/{Path.GetFileName(log)}", log, src, notes);
            }

            if (Newest(src.Folders.Recordings, "*.jsonl", 1).FirstOrDefault() is { } recording)
            {
                if (new FileInfo(recording).Length > MaxRecordingBytes)
                {
                    notes.Add($"recording {Path.GetFileName(recording)} left out: over {MaxRecordingBytes / 1024 / 1024} MB");
                }
                else
                {
                    AddText(zip, $"recording/{Path.GetFileName(recording)}", recording, src, notes);
                    var summary = Path.ChangeExtension(recording, ".md");
                    if (File.Exists(summary))
                    {
                        AddText(zip, $"recording/{Path.GetFileName(summary)}", summary, src, notes);
                    }
                }
            }

            if (src.BatcLogFolder is { } batcLogs)
            {
                foreach (var name in new[] { "Player.log", "Player-prev.log" })
                {
                    var file = Path.Combine(batcLogs, name);
                    if (File.Exists(file))
                    {
                        AddText(zip, $"beyondatc/{name}", file, src, notes, TailBytes);
                    }
                }
            }

            if (src.BatcFolder is { } batc)
            {
                foreach (var file in new BatcRedirect(batc).Files.Where(File.Exists))
                {
                    var where = Path.GetDirectoryName(file)!.Length > batc.Length ? "StreamingAssets-" : "";
                    AddText(zip, $"beyondatc/{where}SimConnect.cfg", file, src, notes);
                }
            }

            if (src.XPlaneFolder is { } xplane && File.Exists(Path.Combine(xplane, "Log.txt")))
            {
                AddText(zip, "xplane/Log.txt", Path.Combine(xplane, "Log.txt"), src, notes, TailBytes);
            }

            var about = zip.CreateEntry("about.txt");
            using var writer = new StreamWriter(about.Open(), new UTF8Encoding(false));
            writer.Write(src.Redactor.Apply(About(src, now, notes)));
        }

        return zipPath;
    }

    private static string About(SupportSources src, DateTime now, List<string> notes)
    {
        var text = new List<string>();
        text.Add($"XPConnect {src.AppVersion} support file, made {now:yyyy-MM-dd HH:mm:ss}");
        text.Add($"Windows: {Environment.OSVersion}, {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}");
        text.Add("");
        if (src.BatcFolder is { } batc)
        {
            text.Add($"BeyondATC: {batc} (version {FileVersion(Path.Combine(batc, "BeyondATC.exe"))})");
            var redirect = new BatcRedirect(batc);
            foreach (var file in redirect.Files)
            {
                text.Add($"  {redirect.StateOf(file),-9} {file}");
            }
        }
        else
        {
            text.Add("BeyondATC: not found");
        }

        if (src.XPlaneFolder is { } xplane)
        {
            var plugin = Path.Combine(xplane, "Resources", "plugins", "XPConnect");
            text.Add($"X-Plane: {xplane}");
            text.Add($"  traffic plugin {(Directory.Exists(plugin) ? "installed" : "not installed")}");
        }
        else
        {
            text.Add("X-Plane: not found");
        }

        var running = WatchedProcesses
            .Where(name => Process.GetProcessesByName(name).Length > 0);
        text.Add($"Running now: {string.Join(", ", running.DefaultIfEmpty("none of MSFS, BeyondATC, X-Plane"))}");
        if (notes.Count > 0)
        {
            text.Add("");
            text.AddRange(notes);
        }

        return string.Join(Environment.NewLine, text) + Environment.NewLine;
    }

    private static string FileVersion(string file)
    {
        if (!File.Exists(file))
        {
            return "unknown";
        }

        var info = FileVersionInfo.GetVersionInfo(file);
        return string.IsNullOrWhiteSpace(info.ProductVersion) ? info.FileVersion ?? "unknown" : info.ProductVersion;
    }

    private static IEnumerable<string> Newest(string folder, string pattern, int count) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, pattern).OrderByDescending(File.GetLastWriteTimeUtc).Take(count)
            : [];

    /// <summary>Copies a text file line by line through the redactor; with tailBytes, only the end of it.</summary>
    private static void AddText(ZipArchive zip, string entryName, string file, SupportSources src, List<string> notes, long tailBytes = 0)
    {
        try
        {
            // Shared read: the log being written right now, or BeyondATC's open Player.log.
            using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var cut = tailBytes > 0 && input.Length > tailBytes;
            if (cut)
            {
                input.Seek(-tailBytes, SeekOrigin.End);
            }

            using var reader = new StreamReader(input);
            if (cut)
            {
                reader.ReadLine(); // drop the partial first line
            }

            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var output = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            if (cut)
            {
                output.WriteLine($"[first {input.Length - tailBytes} bytes left out]");
            }

            while (reader.ReadLine() is { } line)
            {
                output.WriteLine(src.Redactor.Apply(line));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            notes.Add($"{entryName} left out: {ex.Message}");
        }
    }
}

using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace ClearanceLink.Host.Support;

/// <summary>Where the files for a support zip come from. Null folders are skipped.</summary>
public sealed record SupportSources(
    string AppVersion,
    AppFolders Folders,
    AtcApp? App,
    string? XPlaneFolder,
    string? AppLogFolder,
    Redactor Redactor);

/// <summary>
/// Builds one zip a user can send for troubleshooting: about.txt (versions, folders, the ATC app's redirect state), settings,
/// the newest session logs and recording, the ATC app's Player.log (for a Unity app) and the end of X-Plane's Log.txt. Every text file
/// goes through the <see cref="Redactor"/> first.
/// </summary>
public static class SupportBundle
{
    public const int LogsIncluded = 5;
    public const long TailBytes = 5 * 1024 * 1024;
    public const long MaxRecordingBytes = 300L * 1024 * 1024;

    private static readonly string[] WatchedProcesses = ["FlightSimulator2024", "FlightSimulator", "X-Plane"];

    public static string Create(SupportSources src, DateTime now)
    {
        Directory.CreateDirectory(src.Folders.SupportFiles);
        var zipPath = Path.Combine(src.Folders.SupportFiles, $"ClearanceLink-support-{now:yyyyMMdd-HHmmss}.zip");
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

            if (src.AppLogFolder is { } appLogs)
            {
                foreach (var name in new[] { "Player.log", "Player-prev.log" })
                {
                    var file = Path.Combine(appLogs, name);
                    if (File.Exists(file))
                    {
                        AddText(zip, $"app/{name}", file, src, notes, TailBytes);
                    }
                }
            }

            if (src.App is { } app)
            {
                foreach (var file in new AppRedirect(app).Files.Where(File.Exists))
                {
                    var where = Path.GetDirectoryName(file)!.Length > app.Folder.Length ? "StreamingAssets-" : "";
                    AddText(zip, $"app/{where}SimConnect.cfg", file, src, notes);
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
        text.Add($"ClearanceLink {src.AppVersion} support file, made {now:yyyy-MM-dd HH:mm:ss}");
        text.Add($"Windows: {Environment.OSVersion}, {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}");
        text.Add("");
        if (src.App is { } app)
        {
            text.Add($"ATC app: {app.Exe} (version {FileVersion(app.Exe)})");
            var redirect = new AppRedirect(app);
            foreach (var file in redirect.Files)
            {
                text.Add($"  {redirect.StateOf(file),-9} {file}");
            }
        }
        else
        {
            text.Add("ATC app: not chosen");
        }

        if (src.XPlaneFolder is { } xplane)
        {
            var plugin = Path.Combine(xplane, "Resources", "plugins", "ClearanceLink");
            text.Add($"X-Plane: {xplane}");
            text.Add($"  traffic plugin {(Directory.Exists(plugin) ? "installed" : "not installed")}");
        }
        else
        {
            text.Add("X-Plane: not found");
        }

        var running = WatchedProcesses.Concat(src.App is { } chosen ? [chosen.Name] : [])
            .Where(name => Process.GetProcessesByName(name).Length > 0);
        text.Add($"Running now: {string.Join(", ", running.DefaultIfEmpty("none of MSFS, the ATC app, X-Plane"))}");
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
            // Shared read: the log being written right now, or the ATC app's open Player.log.
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

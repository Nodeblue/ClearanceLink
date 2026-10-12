namespace ClearanceLink.Host;

/// <summary>
/// The SimConnect app the user chose to bridge to X-Plane, from its .exe (ADR-0015). Everything ClearanceLink needs
/// from it is worked out from that path: its folder (where SimConnect.cfg goes), and, for a Unity app, its log and saved
/// settings. A Unity build keeps a <c>&lt;Name&gt;_Data</c> folder next to <c>&lt;Name&gt;.exe</c>, whose <c>app.info</c>
/// holds the company and product names Unity uses for <c>%USERPROFILE%\AppData\LocalLow\&lt;company&gt;\&lt;product&gt;\Player.log</c>
/// and the PlayerPrefs key <c>HKCU\Software\&lt;company&gt;\&lt;product&gt;</c>.
/// </summary>
public sealed class AtcApp
{
    private AtcApp(string exe)
    {
        Exe = exe;
        Folder = System.IO.Path.GetDirectoryName(exe)!;
        Name = System.IO.Path.GetFileNameWithoutExtension(exe);
        var data = System.IO.Path.Combine(Folder, Name + "_Data");
        DataFolder = Directory.Exists(data) ? data : null;
        (Company, Product) = ReadAppInfo(DataFolder);
    }

    /// <summary>The app's .exe, full path.</summary>
    public string Exe { get; }

    /// <summary>The folder the .exe is in.</summary>
    public string Folder { get; }

    /// <summary>The .exe's name without .exe, as its process is named.</summary>
    public string Name { get; }

    /// <summary>The Unity data folder (<c>&lt;Name&gt;_Data</c>) next to the .exe; null when the app isn't a Unity app.</summary>
    public string? DataFolder { get; }

    /// <summary>The Unity company name from app.info; null when unknown.</summary>
    public string? Company { get; }

    /// <summary>The Unity product name from app.info; null when unknown.</summary>
    public string? Product { get; }

    /// <summary>The folder Unity writes the app's Player.log to; null when the app isn't a Unity app.</summary>
    public string? LogFolder => Company is null || Product is null
        ? null
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", Company, Product);

    /// <summary>The app's Player.log; null when the app isn't a Unity app.</summary>
    public string? PlayerLog => LogFolder is { } folder ? System.IO.Path.Combine(folder, "Player.log") : null;

    /// <summary>The app's Unity PlayerPrefs under HKEY_CURRENT_USER; null when the app isn't a Unity app.</summary>
    public string? PrefsKey => Company is null || Product is null ? null : $@"Software\{Company}\{Product}";

    /// <summary>The app for this .exe, or null when the path is empty, isn't an .exe, or the file isn't there.</summary>
    public static AtcApp? From(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe))
        {
            return null;
        }

        string full;
        try
        {
            full = System.IO.Path.GetFullPath(exe.Trim().Trim('"'));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return string.Equals(System.IO.Path.GetExtension(full), ".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(full)
            ? new AtcApp(full)
            : null;
    }

    /// <summary>
    /// The app's .exe in a folder saved by an earlier version (which stored the folder, not the .exe): the .exe that has
    /// a matching <c>&lt;Name&gt;_Data</c> folder, else the only .exe there. Null when there isn't exactly one match.
    /// </summary>
    public static string? FindExeIn(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        try
        {
            var exes = Directory.GetFiles(folder, "*.exe");
            var unity = exes.Where(e => Directory.Exists(System.IO.Path.Combine(folder, System.IO.Path.GetFileNameWithoutExtension(e) + "_Data"))).ToList();
            var pick = unity.Count == 1 ? unity : exes.ToList();
            return pick.Count == 1 ? System.IO.Path.GetFullPath(pick[0]) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>app.info: the company name on the first line, the product name on the second.</summary>
    private static (string?, string?) ReadAppInfo(string? dataFolder)
    {
        if (dataFolder is null)
        {
            return (null, null);
        }

        try
        {
            var file = System.IO.Path.Combine(dataFolder, "app.info");
            if (!File.Exists(file))
            {
                return (null, null);
            }

            var lines = File.ReadAllLines(file).Select(l => l.Trim()).ToArray();
            return lines.Length >= 2 && lines[0].Length > 0 && lines[1].Length > 0 && Safe(lines[0]) && Safe(lines[1])
                ? (lines[0], lines[1])
                : (null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    // Each name becomes one folder (and one registry key) name.
    private static bool Safe(string name) =>
        name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0 && name.IndexOfAny(['\\', '/']) < 0 && name is not ("." or "..");
}

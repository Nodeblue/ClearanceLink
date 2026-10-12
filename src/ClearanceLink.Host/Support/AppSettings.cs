using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClearanceLink.Host.Support;

/// <summary>
/// The user's saved choices: folders picked by hand (null means find it automatically), whether to keep a
/// session log, and how many days of logs and recordings to keep. Stored as JSON in <see cref="AppFolders.Settings"/>.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    /// <summary>The .exe of the SimConnect app the user chose to bridge (the ATC app); null until chosen.</summary>
    public string? AppExe { get; set; }

    /// <summary>The app's folder as saved by versions up to 0.7.6; read once to find <see cref="AppExe"/>, never written.</summary>
    [JsonPropertyName(OldNames.AppFolderSetting)]
    public string? OldAppFolder { get; set; }

    /// <summary>X-Plane's folder when the user chose it; null to find it automatically.</summary>
    public string? XPlaneFolder { get; set; }

    /// <summary>FSLTL's fsltl-traffic-base folder when the user chose it; null to find it through MSFS's settings.</summary>
    public string? FsltlFolder { get; set; }

    /// <summary>How the ATC app reaches ClearanceLink: the default MSFS connection (nothing changed in the app) or its config files.</summary>
    public ConnectionMode Connection { get; set; } = ConnectionMode.Automatic;

    /// <summary>Write each session's log to a file (on by default) so it can go in a support file.</summary>
    public bool SessionLog { get; set; } = true;

    /// <summary>Session logs and recordings older than this are deleted at start-up.</summary>
    public int KeepDays { get; set; } = 7;

    /// <summary>Set once the user has been through (or skipped) the window's getting-started pages.</summary>
    public bool GettingStartedDone { get; set; }

    /// <summary>Ask GitHub at start-up whether a newer release exists (only looks; nothing is downloaded).</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// Call Better Pushback's tug as soon as the ATC app approves the pushback, as the app does with GSX in MSFS
    /// (ADR-0013). Off: the PUSH page waits for START PUSH.
    /// </summary>
    public bool PushAutomatic { get; set; } = true;

    /// <summary>SimBrief pilot ID (or username) when the user set one; null to use the ATC app's.</summary>
    public string? SimbriefId { get; set; }

    /// <summary>
    /// Reads the file; a missing or unreadable file gives the defaults, a damaged one is kept as .bad (when it can be)
    /// and gives the defaults. Never throws, so a bad settings file cannot stop the window or a command starting.
    /// </summary>
    public static AppSettings Load(string file)
    {
        string text;
        try
        {
            if (!File.Exists(file))
            {
                return new AppSettings();
            }

            text = File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(text, Json) ?? new AppSettings();
            if (settings.OldAppFolder is { } old)
            {
                // An upgrade from 0.7.6 or earlier: the folder becomes the app's .exe, and is dropped on the next save.
                settings.AppExe ??= AtcApp.FindExeIn(old);
                settings.OldAppFolder = null;
            }

            return settings;
        }
        catch (JsonException)
        {
            try
            {
                File.Copy(file, file + ".bad", overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keeping the damaged file is only a courtesy; the defaults still apply.
            }

            return new AppSettings();
        }
    }

    /// <summary>Writes through a temporary file so a crash never leaves half a settings file.</summary>
    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, file, overwrite: true);
    }
}

public enum ConnectionMode
{
    /// <summary>
    /// The ATC app's SimConnect.cfg files point at ClearanceLink only while it runs and MSFS is closed (ADR-0012); MSFS's
    /// default connection while MSFS is closed when no app is chosen or its folder can't be written.
    /// </summary>
    Automatic,

    /// <summary>The ATC app's SimConnect.cfg files point at ClearanceLink, so it reaches X-Plane even while MSFS is open.</summary>
    AlwaysOn,
}

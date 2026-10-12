namespace ClearanceLink.Host.Support;

/// <summary>
/// Names from earlier versions: from before ClearanceLink was renamed from XPConnect (0.5,
/// docs/decisions/ADR-0009-clearancelink-name.md), and from before the ATC app was chosen by its .exe (0.7.7, ADR-0015).
/// Each is moved to its new name once, or still read, so an upgrade keeps the user's settings, plugin options and
/// converted models.
/// </summary>
public static class OldNames
{
    /// <summary>The settings and logs folder under %LOCALAPPDATA%.</summary>
    public const string AppFolder = "XPConnect";

    /// <summary>The X-Plane plugin folder under Resources\plugins.</summary>
    public const string PluginFolder = "XPConnect";

    /// <summary>Converted FSLTL models under X-Plane 12\Custom Data\CSL.</summary>
    public const string FsltlOutput = "XPConnect-FSLTL";

    /// <summary>The backup of a user's own SimConnect.cfg made when switching ALWAYS ON.</summary>
    public const string CfgBackup = "SimConnect.cfg.xpconnect-backup";

    /// <summary>The settings.json key that held the ATC app's folder up to 0.7.6 (now <see cref="AppSettings.AppExe"/>).</summary>
    public const string AppFolderSetting = "batcFolder";
}

namespace ClearanceLink.Host.Support;

/// <summary>
/// Names from before ClearanceLink was renamed from XPConnect (0.5). Each is moved to its new name once, or still read,
/// so an upgrade keeps the user's settings, plugin options and converted models (docs/decisions/ADR-0009-clearancelink-name.md).
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
}

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

    /// <summary>The app up to 0.7.6 looked for itself under this uninstall name and folder name when no folder was set.</summary>
    public const string AppName = "BeyondATC";

    /// <summary>The traffic-models link folder under ClearanceLink's folder up to 0.7.6 (now "traffic packages").</summary>
    public const string TrafficLinkFolder = AppName + " packages";

    /// <summary>
    /// Where versions up to 0.7.6 found the ATC app when no folder was set: its uninstall entry's install location,
    /// then the usual folders. Read once, so an upgrade keeps working without choosing the app again.
    /// </summary>
    public static IEnumerable<string?> AppFolders()
    {
        if (OperatingSystem.IsWindows())
        {
            const string uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
            foreach (var hive in new[] { Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryHive.LocalMachine })
            {
                foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
                {
                    using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view).OpenSubKey(uninstall);
                    foreach (var name in root?.GetSubKeyNames() ?? [])
                    {
                        using var app = root!.OpenSubKey(name);
                        if (app?.GetValue("DisplayName") is string display && display.StartsWith(AppName, StringComparison.OrdinalIgnoreCase))
                        {
                            yield return app.GetValue("InstallLocation") as string;
                        }
                    }
                }
            }
        }

        yield return Path.Combine(@"C:\", AppName);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
    }
}

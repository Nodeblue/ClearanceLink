namespace ClearanceLink.Host.Support;

/// <summary>Where ClearanceLink keeps its own files: %LOCALAPPDATA%\ClearanceLink\ (settings.json, logs, recordings, support).</summary>
public sealed class AppFolders(string root)
{
    public static AppFolders Default { get; } = Open(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>The ClearanceLink folder under <paramref name="parent"/>, first moving XPConnect's folder there if only that exists.</summary>
    public static AppFolders Open(string parent)
    {
        var root = Path.Combine(parent, "ClearanceLink");
        var old = Path.Combine(parent, OldNames.AppFolder);
        if (!Directory.Exists(root) && Directory.Exists(old))
        {
            try
            {
                Directory.Move(old, root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An old XPConnect still running holds its log open: keep at least the settings.
                Directory.CreateDirectory(root);
                var settings = Path.Combine(old, "settings.json");
                if (File.Exists(settings))
                {
                    File.Copy(settings, Path.Combine(root, "settings.json"));
                }
            }
        }

        return new AppFolders(root);
    }

    public string Root { get; } = root;

    public string Settings => Path.Combine(Root, "settings.json");

    public string Logs => Path.Combine(Root, "logs");

    public string Recordings => Path.Combine(Root, "recordings");

    public string SupportFiles => Path.Combine(Root, "support");

    /// <summary>The cached apt.dat index (rebuilt when any apt.dat file changes).</summary>
    public string AptIndex => Path.Combine(Root, "apt-index.json");
}

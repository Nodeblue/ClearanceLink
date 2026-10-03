namespace XPConnect.Host.Support;

/// <summary>Where XPConnect keeps its own files: %LOCALAPPDATA%\XPConnect\ (settings.json, logs, recordings, support).</summary>
public sealed class AppFolders(string root)
{
    public static AppFolders Default { get; } =
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XPConnect"));

    public string Root { get; } = root;

    public string Settings => Path.Combine(Root, "settings.json");

    public string Logs => Path.Combine(Root, "logs");

    public string Recordings => Path.Combine(Root, "recordings");

    public string SupportFiles => Path.Combine(Root, "support");
}

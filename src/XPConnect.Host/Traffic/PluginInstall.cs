using System.Security.Cryptography;

namespace XPConnect.Host.Traffic;

/// <summary>
/// Copies the bundled X-Plane plugin (<c>X-Plane plugin\XPConnect</c> next to XPConnect.exe) into X-Plane's
/// <c>Resources\plugins\XPConnect</c>, or removes it. The user's <c>csl_paths.txt</c> is kept.
/// </summary>
public sealed class PluginInstall(string xplaneRoot, string? bundledFolder = null)
{
    public const string FolderName = "XPConnect";
    public const string UserFile = "csl_paths.txt";

    public enum State
    {
        /// <summary>This XPConnect has no plugin to install (a source build without the plugin).</summary>
        NotBundled,
        NotInstalled,
        Outdated,
        Installed,
    }

    public string Bundled { get; } = bundledFolder ?? Path.Combine(AppContext.BaseDirectory, "X-Plane plugin", FolderName);

    public string Target { get; } = Path.Combine(xplaneRoot, "Resources", "plugins", FolderName);

    public State Check()
    {
        if (!Directory.Exists(Bundled))
        {
            return State.NotBundled;
        }

        if (!Directory.Exists(Target))
        {
            return State.NotInstalled;
        }

        return Files(Bundled).All(f => SameFile(Path.Combine(Bundled, f), Path.Combine(Target, f))) ? State.Installed : State.Outdated;
    }

    /// <summary>Returns lines for the console. Throws IOException when X-Plane has the plugin loaded.</summary>
    public IReadOnlyList<string> Install()
    {
        if (!Directory.Exists(Bundled))
        {
            return [$"No plugin next to XPConnect ({Bundled})."];
        }

        foreach (var file in Files(Bundled))
        {
            var to = Path.Combine(Target, file);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(Path.Combine(Bundled, file), to, overwrite: true);
        }

        return [$"Installed the XPConnect plugin in {Target}"];
    }

    public IReadOnlyList<string> Remove()
    {
        if (!Directory.Exists(Target))
        {
            return ["The XPConnect plugin is not installed."];
        }

        var keep = Path.Combine(Target, UserFile);
        string? kept = File.Exists(keep) ? File.ReadAllText(keep) : null;
        Directory.Delete(Target, recursive: true);
        return kept is not null && kept.Trim().Length > 0
            ? [$"Removed {Target}", $"Your CSL folder list was:{Environment.NewLine}{kept.Trim()}"]
            : [$"Removed {Target}"];
    }

    private static IEnumerable<string> Files(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f))
            .Where(f => !f.Equals(UserFile, StringComparison.OrdinalIgnoreCase));

    private static bool SameFile(string a, string b)
    {
        if (!File.Exists(b) || new FileInfo(a).Length != new FileInfo(b).Length)
        {
            return false;
        }

        using var fa = File.OpenRead(a);
        using var fb = File.OpenRead(b);
        return SHA256.HashData(fa).AsSpan().SequenceEqual(SHA256.HashData(fb));
    }
}

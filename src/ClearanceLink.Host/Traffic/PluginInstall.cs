using System.Security.Cryptography;

namespace ClearanceLink.Host.Traffic;

/// <summary>
/// Copies the bundled X-Plane plugin (<c>X-Plane plugin\ClearanceLink</c> next to ClearanceLink.exe) into X-Plane's
/// <c>Resources\plugins\ClearanceLink</c>, or removes it. The user's <c>csl_paths.txt</c> is kept. The plugin from before
/// the rename (<c>Resources\plugins\XPConnect</c>) counts as outdated: installing moves its user files across and
/// removes it, so X-Plane never loads both.
/// </summary>
public sealed class PluginInstall(string xplaneRoot, string? bundledFolder = null)
{
    public const string FolderName = "ClearanceLink";
    public const string UserFile = "csl_paths.txt";

    /// <summary>Files and folders the user or the plugin itself made in the plugin folder: kept across the rename.</summary>
    private static readonly string[] UserItems = [UserFile, "settings.txt", Path.Combine("Resources", "CSL")];

    public enum State
    {
        /// <summary>This ClearanceLink has no plugin to install (a source build without the plugin).</summary>
        NotBundled,
        NotInstalled,
        Outdated,
        Installed,
    }

    public string Bundled { get; } = bundledFolder ?? Path.Combine(AppContext.BaseDirectory, "X-Plane plugin", FolderName);

    public string Target { get; } = Path.Combine(xplaneRoot, "Resources", "plugins", FolderName);

    /// <summary>Where the plugin was installed before the rename.</summary>
    public string OldTarget { get; } = Path.Combine(xplaneRoot, "Resources", "plugins", Support.OldNames.PluginFolder);

    public State Check()
    {
        if (!Directory.Exists(Bundled))
        {
            return State.NotBundled;
        }

        if (!Directory.Exists(Target))
        {
            return Directory.Exists(OldTarget) ? State.Outdated : State.NotInstalled;
        }

        if (Directory.Exists(OldTarget))
        {
            return State.Outdated;
        }

        return Files(Bundled).All(f => SameFile(Path.Combine(Bundled, f), Path.Combine(Target, f))) ? State.Installed : State.Outdated;
    }

    /// <summary>Returns lines for the console. Throws IOException when X-Plane has the plugin loaded.</summary>
    public IReadOnlyList<string> Install()
    {
        if (!Directory.Exists(Bundled))
        {
            return [$"No plugin next to ClearanceLink ({Bundled})."];
        }

        // Stop before copying anything while X-Plane has either plugin loaded: otherwise the new plugin is copied,
        // the old folder can't be deleted, and X-Plane loads both next time.
        ThrowIfLoaded(Target);
        ThrowIfLoaded(OldTarget);

        foreach (var file in Files(Bundled))
        {
            var to = Path.Combine(Target, file);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(Path.Combine(Bundled, file), to, overwrite: true);
        }

        var lines = new List<string> { $"Installed the ClearanceLink plugin in {Target}" };
        if (Directory.Exists(OldTarget))
        {
            foreach (var item in UserItems)
            {
                var from = Path.Combine(OldTarget, item);
                var to = Path.Combine(Target, item);
                if (File.Exists(from) && !File.Exists(to))
                {
                    File.Copy(from, to);
                }
                else if (Directory.Exists(from) && !Directory.Exists(to))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    Directory.Move(from, to);
                }
            }

            Directory.Delete(OldTarget, recursive: true);
            lines.Add($"Removed the old XPConnect plugin from {OldTarget}");
        }

        return lines;
    }

    public IReadOnlyList<string> Remove()
    {
        // The user's CSL folder list, from the new folder or else the old XPConnect one, is shown so it isn't lost.
        var kept = new[] { Target, OldTarget }.Select(f => Path.Combine(f, UserFile)).Where(File.Exists).Select(File.ReadAllText).FirstOrDefault();
        if (Directory.Exists(OldTarget))
        {
            Directory.Delete(OldTarget, recursive: true);
        }

        if (!Directory.Exists(Target))
        {
            return kept is not null && kept.Trim().Length > 0
                ? ["Removed the old XPConnect plugin.", $"Your CSL folder list was:{Environment.NewLine}{kept.Trim()}"]
                : ["The ClearanceLink plugin is not installed."];
        }

        Directory.Delete(Target, recursive: true);
        return kept is not null && kept.Trim().Length > 0
            ? [$"Removed {Target}", $"Your CSL folder list was:{Environment.NewLine}{kept.Trim()}"]
            : [$"Removed {Target}"];
    }

    /// <summary>Throws IOException when a plugin binary in <paramref name="folder"/> is open (X-Plane has it loaded).</summary>
    private static void ThrowIfLoaded(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var xpl in Directory.EnumerateFiles(folder, "*.xpl", SearchOption.AllDirectories))
        {
            using var _ = new FileStream(xpl, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
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

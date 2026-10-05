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

    /// <summary>Why an install, update or removal failed; see <see cref="Classify"/>.</summary>
    public enum Failure
    {
        /// <summary>X-Plane has the plugin loaded (a sharing violation): close X-Plane and try again.</summary>
        XPlaneRunning,

        /// <summary>No permission to write in X-Plane's plugins folder: run as administrator or fix the folder's permissions.</summary>
        NoPermission,

        /// <summary>Anything else (disk full, damaged file): show the message.</summary>
        Other,
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

    /// <summary>
    /// Tells apart the exceptions <see cref="Install"/> and <see cref="Remove"/> throw: <see cref="PluginInUseException"/>
    /// or a sharing violation is X-Plane running; <see cref="UnauthorizedAccessException"/> is a permissions problem.
    /// </summary>
    public static Failure Classify(Exception ex) => ex switch
    {
        PluginInUseException => Failure.XPlaneRunning,
        UnauthorizedAccessException => Failure.NoPermission,
        IOException io when IsSharingViolation(io) => Failure.XPlaneRunning,
        _ => Failure.Other,
    };

    /// <summary>A one-line message for the console and the log.</summary>
    public string Describe(Exception ex) => Classify(ex) switch
    {
        Failure.XPlaneRunning => $"X-Plane has the plugin loaded ({ex.Message}). Close X-Plane, then try again.",
        Failure.NoPermission => $"no permission to write in {Path.GetDirectoryName(Target)} ({ex.Message}). Run ClearanceLink as administrator, or make the X-Plane folder writable.",
        _ => ex.Message,
    };

    /// <summary>
    /// Returns lines for the console. Throws <see cref="PluginInUseException"/> when X-Plane has the plugin loaded,
    /// <see cref="UnauthorizedAccessException"/> when the folder is not writable, other IOExceptions otherwise.
    /// </summary>
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
            try
            {
                using var _ = new FileStream(xpl, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when (ex is not FileNotFoundException and not DirectoryNotFoundException)
            {
                throw new PluginInUseException(xpl, ex);
            }
        }
    }

    // ERROR_SHARING_VIOLATION and ERROR_LOCK_VIOLATION as HRESULTs.
    private static bool IsSharingViolation(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33 && (ex.HResult >> 16 & 0x1FFF) == 7;

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

/// <summary>A plugin binary is open in another process (X-Plane has the plugin loaded).</summary>
public sealed class PluginInUseException(string file, Exception inner)
    : IOException($"{file} is in use (X-Plane has the plugin loaded)", inner)
{
    public string FilePath { get; } = file;
}

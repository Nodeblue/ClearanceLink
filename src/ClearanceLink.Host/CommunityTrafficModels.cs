using ClearanceLink.Fsltl;

namespace ClearanceLink.Host;

/// <summary>
/// The ATC app lists its traffic models only from MSFS 2024's Community folder: it
/// reads <c>InstalledPackagesPath</c> from MSFS 2024's UserCfg.opt (the Microsoft Store copy, else the Steam copy) and
/// looks in <c>&lt;path&gt;\community</c> for a folder whose name contains fsltl-traffic-base (or AIG's or FS Traffic's).
/// With no MSFS 2024 on the PC it finds none and says "No traffic liveries found". So while ClearanceLink runs, when
/// MSFS 2024 has no UserCfg.opt, this writes one in the Steam place pointing at the FSLTL package ClearanceLink uses,
/// and removes it again on close (ADR-0014). A real MSFS 2024's files are never changed.
/// </summary>
public sealed class CommunityTrafficModels(string storeUserCfg, string steamUserCfg, string linkFolder, int? ownerPid = null, Func<int, bool>? isAlive = null)
{
    private const string FolderMadeLine = "// ClearanceLink made this folder";

    private readonly int _owner = ownerPid ?? Environment.ProcessId;
    private readonly Func<int, bool> _isAlive = isAlive ?? ProcessAlive;

    /// <summary>Folder names the ATC app takes as traffic model packages (it checks "contains", lower case).</summary>
    public static readonly IReadOnlyList<string> PackageNames =
        [FsltlCatalog.PackageName, "aig-aitraffic-oci", "justflight-aircraft-traffic-fleet", "justflight-fstraffic", "gamod1-models"];

    public enum Outcome
    {
        /// <summary>MSFS 2024's Community folder already holds traffic models; nothing changed.</summary>
        Found,

        /// <summary>No MSFS 2024: ClearanceLink's UserCfg.opt now points the ATC app at FSLTL.</summary>
        Pointed,

        /// <summary>MSFS 2024 is installed but its Community folder has no traffic models; the ATC app shows no traffic.</summary>
        NotInMsfs2024,

        /// <summary>No MSFS 2024 and ClearanceLink doesn't know an FSLTL folder; the ATC app shows no traffic.</summary>
        NoFsltl,

        /// <summary>The file or link could not be written.</summary>
        Failed,

        /// <summary>Another ClearanceLink that is still running already wrote the file; it removes it.</summary>
        PointedByAnother,
    }

    public sealed record Result(Outcome Outcome, string Line);

    /// <summary>MSFS 2024's two UserCfg.opt places, and a link folder under ClearanceLink's own folder.</summary>
    public static CommunityTrafficModels Default
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return new CommunityTrafficModels(
                Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
                Path.Combine(roaming, "Microsoft Flight Simulator 2024", "UserCfg.opt"),
                Path.Combine(Support.AppFolders.Default.Root, "traffic packages"));
        }
    }

    /// <summary>The UserCfg.opt ClearanceLink writes when there is no MSFS 2024 (the Steam place).</summary>
    public string StandInFile => steamUserCfg;

    /// <summary>Whether a Community folder holds a package the ATC app takes as traffic models.</summary>
    public static bool HasTrafficModels(string community)
    {
        try
        {
            return Directory.Exists(community) && Directory.EnumerateDirectories(community)
                .Select(d => Path.GetFileName(d).ToLowerInvariant())
                .Any(name => PackageNames.Any(name.Contains));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// What <see cref="Provide"/> would do, without writing anything: for the window's warning that the ATC app will
    /// find no traffic models (<see cref="Outcome.NotInMsfs2024"/> or <see cref="Outcome.NoFsltl"/>).
    /// </summary>
    public Outcome Check(string? fsltl)
    {
        try
        {
            if (MsfsUserCfg() is { } cfg)
            {
                var packages = FsltlLocator.InstalledPackagesPath(File.ReadAllText(cfg));
                return packages is not null && HasTrafficModels(Path.Combine(packages, "Community")) ? Outcome.Found : Outcome.NotInMsfs2024;
            }

            return fsltl is null ? Outcome.NoFsltl : Outcome.Pointed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Outcome.Failed;
        }
    }

    /// <summary>
    /// Makes sure the ATC app will find FSLTL (<paramref name="fsltl"/>: the fsltl-traffic-base folder ClearanceLink uses,
    /// or null). Never throws for a file problem: <see cref="Outcome.Failed"/>.
    /// </summary>
    public Result Provide(string? fsltl)
    {
        try
        {
            if (MsfsUserCfg() is { } cfg)
            {
                // A stand-in left by a crash goes now that MSFS 2024 has its own file.
                Remove(onlyOurs: false);
                var (community, nextBoot) = Where();
                // MSFS 2024 can also name an InstalledPackagesPathNextBoot folder; the ATC app doesn't read it (issue #29).
                var notRead = nextBoot is not null && !string.Equals(nextBoot, community, StringComparison.OrdinalIgnoreCase)
                    ? $"; UserCfg.opt's InstalledPackagesPathNextBoot names {nextBoot}{(HasTrafficModels(nextBoot) ? ", which has traffic models" : "")}, but the ATC app doesn't read that line"
                    : "";
                return community is not null && HasTrafficModels(community)
                    ? new(Outcome.Found, $"traffic-models: the ATC app finds traffic models in MSFS 2024's {community} ({cfg})")
                    : new(Outcome.NotInMsfs2024, $"traffic-models: the ATC app looks for traffic models only in MSFS 2024's {community ?? cfg} ({cfg}) and there are none{notRead}; install FSLTL for MSFS 2024 there or the ATC app shows no traffic");
            }

            if (OwnerOf(steamUserCfg) is { } other && other != _owner && _isAlive(other))
            {
                return new(Outcome.PointedByAnother, $"traffic-models: another ClearanceLink (pid {other}) already points the ATC app at FSLTL");
            }

            if (fsltl is null)
            {
                Remove();
                return new(Outcome.NoFsltl, "traffic-models: no MSFS 2024 and no FSLTL folder; the ATC app shows no traffic until FSLTL is set up on the TRAFFIC page");
            }

            fsltl = Path.GetFullPath(fsltl);
            if (fsltl.StartsWith(@"\\", StringComparison.Ordinal) || fsltl.Contains('%', StringComparison.Ordinal) || fsltl.Contains('"', StringComparison.Ordinal))
            {
                return new(Outcome.Failed, $"traffic-models: can't point the ATC app at FSLTL in {fsltl} (a network folder, or a % or \" in its name); move FSLTL to a local folder without those");
            }

            var target = PackagesFolderFor(fsltl);
            var folder = Path.GetDirectoryName(steamUserCfg)!;
            var folderMade = File.Exists(steamUserCfg)
                ? File.ReadAllText(steamUserCfg).Contains(FolderMadeLine, StringComparison.Ordinal)
                : !Directory.Exists(folder);
            Directory.CreateDirectory(folder);
            File.WriteAllText(steamUserCfg,
                $"{FsltlLocator.StandInMarker} (pid {_owner})\r\n{(folderMade ? FolderMadeLine + "\r\n" : "")}InstalledPackagesPath \"{target}\"\r\n");
            return new(Outcome.Pointed, $"traffic-models: no MSFS 2024; the ATC app pointed at FSLTL in {fsltl} until ClearanceLink closes ({steamUserCfg})");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(Outcome.Failed, $"traffic-models: could not point the ATC app at FSLTL ({ex.Message}); the ATC app may show no traffic");
        }
    }

    /// <summary>
    /// Removes ClearanceLink's UserCfg.opt (never MSFS's own): this ClearanceLink's, or one whose ClearanceLink has
    /// gone; with <paramref name="onlyOurs"/> false, any. Its folder goes too when ClearanceLink made it and it is left
    /// empty. Returns a line, or null when there was nothing to remove.
    /// </summary>
    public string? Remove(bool onlyOurs = true)
    {
        try
        {
            if (!File.Exists(steamUserCfg))
            {
                return RemoveLink() ? $"traffic-models: removed the link in {linkFolder}" : null;
            }

            var text = File.ReadAllText(steamUserCfg);
            if (!FsltlLocator.IsStandIn(text)
                || onlyOurs && OwnerOf(steamUserCfg) is { } owner && owner != _owner && _isAlive(owner))
            {
                return null;
            }

            File.Delete(steamUserCfg);
            var folder = Path.GetDirectoryName(steamUserCfg)!;
            if (text.Contains(FolderMadeLine, StringComparison.Ordinal) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }

            RemoveLink();
            return $"traffic-models: removed {steamUserCfg}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"traffic-models: could not remove {steamUserCfg} ({ex.Message})";
        }
    }

    /// <summary>
    /// Removes the junction to FSLTL (only the link: the package stays) and the folders around it once empty, so
    /// deleting ClearanceLink's folder can never reach FSLTL through it (Windows PowerShell 5.1's Remove-Item -Recurse
    /// follows junctions). Provide makes it again in milliseconds.
    /// </summary>
    private bool RemoveLink() => RemoveLink(linkFolder);

    /// <summary>
    /// Removes the link a version up to 0.7.6 made under its old folder name (<see cref="Support.OldNames.TrafficLinkFolder"/>),
    /// next to <c>linkFolder</c>, so it can't be followed into FSLTL. Returns a line, or null when there was none.
    /// </summary>
    public string? RemoveOldLink()
    {
        var old = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(linkFolder))!, Support.OldNames.TrafficLinkFolder);
        try
        {
            return RemoveLink(old) ? $"traffic-models: removed the link an earlier version left in {old}" : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"traffic-models: could not remove the link in {old} ({ex.Message})";
        }
    }

    private static bool RemoveLink(string linkFolder)
    {
        var link = new DirectoryInfo(Path.Combine(linkFolder, "Community", FsltlCatalog.PackageName));
        if (link.LinkTarget is null)
        {
            return false;
        }

        link.Delete();
        foreach (var folder in new[] { Path.Combine(linkFolder, "Community"), linkFolder })
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }

        return true;
    }

    /// <summary>The process id written in ClearanceLink's UserCfg.opt, or null (not ours, or none).</summary>
    private static int? OwnerOf(string file)
    {
        if (!File.Exists(file))
        {
            return null;
        }

        var first = File.ReadLines(file).FirstOrDefault() ?? "";
        var at = first.LastIndexOf("(pid ", StringComparison.Ordinal);
        return FsltlLocator.IsStandIn(first) && at >= 0
            && int.TryParse(first[(at + 5)..].TrimEnd(')'), System.Globalization.CultureInfo.InvariantCulture, out var pid)
            ? pid
            : null;
    }

    private static bool ProcessAlive(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>MSFS 2024's own UserCfg.opt, the way the ATC app picks it (Store first), ignoring ClearanceLink's.</summary>
    /// <summary>
    /// With MSFS 2024 installed: the Community folder the ATC app reads (null when UserCfg.opt names none), and the one
    /// UserCfg.opt's InstalledPackagesPathNextBoot names, if any (the ATC app doesn't read that line). Both null without
    /// MSFS 2024 or when the file can't be read.
    /// </summary>
    public (string? Community, string? NextBoot) Where()
    {
        try
        {
            if (MsfsUserCfg() is not { } cfg) return (null, null);
            var text = File.ReadAllText(cfg);
            return (FsltlLocator.InstalledPackagesPath(text) is { } p ? Path.Combine(p, "Community") : null,
                FsltlLocator.InstalledPackagesPathNextBoot(text) is { } n ? Path.Combine(n, "Community") : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    private string? MsfsUserCfg() =>
        new[] { storeUserCfg, steamUserCfg }.FirstOrDefault(f => File.Exists(f) && !FsltlLocator.IsStandIn(File.ReadAllText(f)));

    /// <summary>
    /// The folder to name as InstalledPackagesPath: the one above a Community folder holding FSLTL (an MSFS 2020 install),
    /// else ClearanceLink's link folder, whose Community\fsltl-traffic-base links to the package.
    /// </summary>
    private string PackagesFolderFor(string fsltl)
    {
        var parent = Path.GetDirectoryName(fsltl);
        if (parent is not null && Path.GetFileName(parent).Equals("Community", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(fsltl).Contains(FsltlCatalog.PackageName, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(parent)!;
        }

        var link = Path.Combine(linkFolder, "Community", FsltlCatalog.PackageName);
        var existing = new DirectoryInfo(link);
        if (existing.Exists || existing.LinkTarget is not null)
        {
            if (existing.LinkTarget is { } to && Path.GetFullPath(to, Path.GetDirectoryName(link)!).TrimEnd('\\', '/').Equals(fsltl.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                return linkFolder;
            }

            // A link to an older FSLTL folder: deleting the link leaves the package alone.
            existing.Delete();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        MakeLink(link, fsltl);
        return linkFolder;
    }

    /// <summary>A directory junction on Windows (no administrator or developer mode needed), a symbolic link elsewhere.</summary>
    private static void MakeLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        // Both paths quoted by hand: cmd would split an unquoted path at & ( ) ^ (Provide refuses % and ").
        using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/d /s /c \"mklink /J \"{link}\" \"{target}\"\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new IOException("mklink did not start");
        var error = mklink.StandardError.ReadToEnd();
        mklink.WaitForExit();
        if (mklink.ExitCode != 0 || !Directory.Exists(link))
        {
            throw new IOException($"mklink /J failed: {error.Trim()}");
        }
    }
}

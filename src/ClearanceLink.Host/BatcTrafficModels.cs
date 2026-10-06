using ClearanceLink.Fsltl;

namespace ClearanceLink.Host;

/// <summary>
/// BeyondATC lists its traffic models only from MSFS 2024's Community folder: it
/// reads <c>InstalledPackagesPath</c> from MSFS 2024's UserCfg.opt (the Microsoft Store copy, else the Steam copy) and
/// looks in <c>&lt;path&gt;\community</c> for a folder whose name contains fsltl-traffic-base (or AIG's or FS Traffic's).
/// With no MSFS 2024 on the PC it finds none and says "No traffic liveries found". So while ClearanceLink runs, when
/// MSFS 2024 has no UserCfg.opt, this writes one in the Steam place pointing at the FSLTL package ClearanceLink uses,
/// and removes it again on close (ADR-0014). A real MSFS 2024's files are never changed.
/// </summary>
public sealed class BatcTrafficModels(string storeUserCfg, string steamUserCfg, string linkFolder, int? ownerPid = null, Func<int, bool>? isAlive = null)
{
    private const string FolderMadeLine = "// ClearanceLink made this folder";

    private readonly int _owner = ownerPid ?? Environment.ProcessId;
    private readonly Func<int, bool> _isAlive = isAlive ?? ProcessAlive;

    /// <summary>Folder names BeyondATC takes as traffic model packages (it checks "contains", lower case).</summary>
    public static readonly IReadOnlyList<string> PackageNames =
        [FsltlCatalog.PackageName, "aig-aitraffic-oci", "justflight-aircraft-traffic-fleet", "justflight-fstraffic", "gamod1-models"];

    public enum Outcome
    {
        /// <summary>MSFS 2024's Community folder already holds traffic models; nothing changed.</summary>
        Found,

        /// <summary>No MSFS 2024: ClearanceLink's UserCfg.opt now points BeyondATC at FSLTL.</summary>
        Pointed,

        /// <summary>MSFS 2024 is installed but its Community folder has no traffic models; BeyondATC shows no traffic.</summary>
        NotInMsfs2024,

        /// <summary>No MSFS 2024 and ClearanceLink doesn't know an FSLTL folder; BeyondATC shows no traffic.</summary>
        NoFsltl,

        /// <summary>The file or link could not be written.</summary>
        Failed,

        /// <summary>Another ClearanceLink that is still running already wrote the file; it removes it.</summary>
        PointedByAnother,
    }

    public sealed record Result(Outcome Outcome, string Line);

    /// <summary>MSFS 2024's two UserCfg.opt places, and a link folder under ClearanceLink's own folder.</summary>
    public static BatcTrafficModels Default
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return new BatcTrafficModels(
                Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
                Path.Combine(roaming, "Microsoft Flight Simulator 2024", "UserCfg.opt"),
                Path.Combine(Support.AppFolders.Default.Root, "BeyondATC packages"));
        }
    }

    /// <summary>The UserCfg.opt ClearanceLink writes when there is no MSFS 2024 (the Steam place).</summary>
    public string StandInFile => steamUserCfg;

    /// <summary>Whether a Community folder holds a package BeyondATC takes as traffic models.</summary>
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
    /// What <see cref="Provide"/> would do, without writing anything: for the window's warning that BeyondATC will
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
    /// Makes sure BeyondATC will find FSLTL (<paramref name="fsltl"/>: the fsltl-traffic-base folder ClearanceLink uses,
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
                var packages = FsltlLocator.InstalledPackagesPath(File.ReadAllText(cfg));
                var community = packages is null ? null : Path.Combine(packages, "Community");
                return community is not null && HasTrafficModels(community)
                    ? new(Outcome.Found, $"batc-models: BeyondATC finds traffic models in MSFS 2024's {community}")
                    : new(Outcome.NotInMsfs2024, $"batc-models: BeyondATC looks for traffic models only in MSFS 2024's {community ?? cfg} and there are none; install FSLTL for MSFS 2024 there or BeyondATC shows no traffic");
            }

            if (OwnerOf(steamUserCfg) is { } other && other != _owner && _isAlive(other))
            {
                return new(Outcome.PointedByAnother, $"batc-models: another ClearanceLink (pid {other}) already points BeyondATC at FSLTL");
            }

            if (fsltl is null)
            {
                Remove();
                return new(Outcome.NoFsltl, "batc-models: no MSFS 2024 and no FSLTL folder; BeyondATC shows no traffic until FSLTL is set up on the TRAFFIC page");
            }

            fsltl = Path.GetFullPath(fsltl);
            if (fsltl.StartsWith(@"\\", StringComparison.Ordinal) || fsltl.Contains('%', StringComparison.Ordinal) || fsltl.Contains('"', StringComparison.Ordinal))
            {
                return new(Outcome.Failed, $"batc-models: can't point BeyondATC at FSLTL in {fsltl} (a network folder, or a % or \" in its name); move FSLTL to a local folder without those");
            }

            var target = PackagesFolderFor(fsltl);
            var folder = Path.GetDirectoryName(steamUserCfg)!;
            var folderMade = File.Exists(steamUserCfg)
                ? File.ReadAllText(steamUserCfg).Contains(FolderMadeLine, StringComparison.Ordinal)
                : !Directory.Exists(folder);
            Directory.CreateDirectory(folder);
            File.WriteAllText(steamUserCfg,
                $"{FsltlLocator.StandInMarker} (pid {_owner})\r\n{(folderMade ? FolderMadeLine + "\r\n" : "")}InstalledPackagesPath \"{target}\"\r\n");
            return new(Outcome.Pointed, $"batc-models: no MSFS 2024; BeyondATC pointed at FSLTL in {fsltl} until ClearanceLink closes ({steamUserCfg})");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(Outcome.Failed, $"batc-models: could not point BeyondATC at FSLTL ({ex.Message}); BeyondATC may show no traffic");
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
                return null;
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

            return $"batc-models: removed {steamUserCfg}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"batc-models: could not remove {steamUserCfg} ({ex.Message})";
        }
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

    /// <summary>MSFS 2024's own UserCfg.opt, the way BeyondATC picks it (Store first), ignoring ClearanceLink's.</summary>
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

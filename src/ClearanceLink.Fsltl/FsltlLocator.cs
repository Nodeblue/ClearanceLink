namespace ClearanceLink.Fsltl;

/// <summary>Finds the FSLTL package in the MSFS 2024 or 2020 Community folder, from MSFS's UserCfg.opt.</summary>
public static partial class FsltlLocator
{
    /// <summary>UserCfg.opt locations: MSFS 2024 and 2020, Microsoft Store and Steam.</summary>
    public static IEnumerable<string> UserCfgCandidates()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt");
        yield return Path.Combine(roaming, "Microsoft Flight Simulator 2024", "UserCfg.opt");
        yield return Path.Combine(local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt");
        yield return Path.Combine(roaming, "Microsoft Flight Simulator", "UserCfg.opt");
    }

    /// <summary>
    /// The <c>InstalledPackagesPath</c> of a UserCfg.opt, or null. Read as the ATC app reads it,
    /// so an
    /// <c>InstalledPackagesPathNextBoot</c> line, which the ATC app doesn't read, is not taken for it (issue #29).
    /// </summary>
    public static string? InstalledPackagesPath(string userCfgText) =>
        PackagesPath().Match(userCfgText) is { Success: true } m && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : null;

    /// <summary>The <c>InstalledPackagesPathNextBoot</c> of a UserCfg.opt, or null. Not read by the ATC app.</summary>
    public static string? InstalledPackagesPathNextBoot(string userCfgText) =>
        NextBootPath().Match(userCfgText) is { Success: true } m && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : null;

    [System.Text.RegularExpressions.GeneratedRegex("InstalledPackagesPath\\s+\"([^\"]*)\"")]
    private static partial System.Text.RegularExpressions.Regex PackagesPath();

    [System.Text.RegularExpressions.GeneratedRegex("InstalledPackagesPathNextBoot\\s+\"([^\"]*)\"")]
    private static partial System.Text.RegularExpressions.Regex NextBootPath();

    /// <summary>
    /// The FSLTL package for a folder the user chose (see <see cref="FromChosen"/>), or with none given, the first
    /// fsltl-traffic-base found through UserCfg.opt.
    /// </summary>
    public static string? Find(string? given = null, IEnumerable<string>? userCfgs = null)
    {
        if (given is not null) return FromChosen(given);
        foreach (var cfg in userCfgs ?? UserCfgCandidates())
        {
            if (!File.Exists(cfg)) continue;
            var text = File.ReadAllText(cfg);
            if (IsStandIn(text)) continue;
            // NextBoot too: MSFS can name a new packages folder there (issue #29). FSLTL found only there is still
            // converted; FSLTL CHECK then says the ATC app won't see it, since the app reads InstalledPackagesPath only.
            foreach (var packages in new[] { InstalledPackagesPath(text), InstalledPackagesPathNextBoot(text) })
            {
                if (packages is null) continue;
                var candidate = Path.Combine(packages, "Community", FsltlCatalog.PackageName);
                if (IsPackage(candidate)) return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// The package for a folder the user picked: the package itself, the Community folder (or the packages folder
    /// above it) holding fsltl-traffic-base, or any folder inside fsltl-traffic-base. Null when none of these.
    /// </summary>
    public static string? FromChosen(string folder)
    {
        folder = Path.TrimEndingDirectorySeparator(folder);
        if (IsPackage(folder)) return folder;
        foreach (var child in new[] { FsltlCatalog.PackageName, $"Community/{FsltlCatalog.PackageName}" })
        {
            if (FileLookup.Resolve(folder, child) is { } inside && IsPackage(inside)) return inside;
        }

        for (var up = Path.GetDirectoryName(folder); up is not null; up = Path.GetDirectoryName(up))
        {
            if (Path.GetFileName(up).Equals(FsltlCatalog.PackageName, StringComparison.OrdinalIgnoreCase) && IsPackage(up)) return up;
        }

        return null;
    }

    /// <summary>Whether MSFS 2020 or 2024 has ever run on this PC (it writes UserCfg.opt); FSLTL is installed through MSFS.</summary>
    public static bool MsfsInstalled(IEnumerable<string>? userCfgs = null) =>
        (userCfgs ?? UserCfgCandidates()).Any(f => File.Exists(f) && !IsStandIn(File.ReadAllText(f)));

    /// <summary>First line of the UserCfg.opt ClearanceLink writes for the ATC app while it runs (no MSFS 2024 on the PC).</summary>
    public const string StandInMarker = "// ClearanceLink: lets the ATC app find FSLTL while ClearanceLink is open; removed when it closes";

    /// <summary>
    /// Whether a UserCfg.opt is ClearanceLink's stand-in for the ATC app, not one MSFS wrote. Earlier versions named
    /// the app in the first line, so only its start and end are compared.
    /// </summary>
    public static bool IsStandIn(string userCfgText)
    {
        var first = userCfgText.Split('\n', 2)[0];
        return first.StartsWith("// ClearanceLink: lets ", StringComparison.Ordinal)
            && first.Contains(" find FSLTL while ClearanceLink is open; removed when it closes", StringComparison.Ordinal);
    }

    public static bool IsPackage(string folder) =>
        Directory.Exists(folder) && FileLookup.Resolve(folder, "SimObjects/Airplanes") is { } airplanes && Directory.Exists(airplanes);
}

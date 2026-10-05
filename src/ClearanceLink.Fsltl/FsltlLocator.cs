namespace ClearanceLink.Fsltl;

/// <summary>Finds the FSLTL package in the MSFS 2024 or 2020 Community folder, from MSFS's UserCfg.opt.</summary>
public static class FsltlLocator
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

    /// <summary>The <c>InstalledPackagesPath</c> line of a UserCfg.opt, or null.</summary>
    public static string? InstalledPackagesPath(string userCfgText)
    {
        foreach (var raw in userCfgText.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("InstalledPackagesPath", StringComparison.OrdinalIgnoreCase)) continue;
            var value = line["InstalledPackagesPath".Length..].Trim().Trim('"');
            return value.Length > 0 ? value : null;
        }

        return null;
    }

    /// <summary>
    /// The FSLTL package for a folder the user chose (see <see cref="FromChosen"/>), or with none given, the first
    /// fsltl-traffic-base found through UserCfg.opt.
    /// </summary>
    public static string? Find(string? given = null, IEnumerable<string>? userCfgs = null)
    {
        if (given is not null) return FromChosen(given);
        foreach (var cfg in userCfgs ?? UserCfgCandidates())
        {
            if (!File.Exists(cfg) || InstalledPackagesPath(File.ReadAllText(cfg)) is not { } packages) continue;
            var candidate = Path.Combine(packages, "Community", FsltlCatalog.PackageName);
            if (IsPackage(candidate)) return candidate;
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
    public static bool MsfsInstalled(IEnumerable<string>? userCfgs = null) => (userCfgs ?? UserCfgCandidates()).Any(File.Exists);

    public static bool IsPackage(string folder) =>
        Directory.Exists(folder) && FileLookup.Resolve(folder, "SimObjects/Airplanes") is { } airplanes && Directory.Exists(airplanes);
}

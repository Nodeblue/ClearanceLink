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

    /// <summary>The given folder if it is an FSLTL package, else the first fsltl-traffic-base found through UserCfg.opt.</summary>
    public static string? Find(string? given = null, IEnumerable<string>? userCfgs = null)
    {
        if (given is not null) return IsPackage(given) ? given : null;
        foreach (var cfg in userCfgs ?? UserCfgCandidates())
        {
            if (!File.Exists(cfg) || InstalledPackagesPath(File.ReadAllText(cfg)) is not { } packages) continue;
            var candidate = Path.Combine(packages, "Community", FsltlCatalog.PackageName);
            if (IsPackage(candidate)) return candidate;
        }

        return null;
    }

    public static bool IsPackage(string folder) =>
        Directory.Exists(folder) && FileLookup.Resolve(folder, "SimObjects/Airplanes") is { } airplanes && Directory.Exists(airplanes);
}

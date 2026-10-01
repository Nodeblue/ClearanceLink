namespace XPConnect.Airports;

/// <summary>
/// Where an X-Plane 12 installation keeps the files XPConnect reads: airport files in scenery priority order,
/// nav data and CIFP procedures (Custom Data, when a navdata update is installed, before X-Plane's default data).
/// </summary>
public sealed class XPlaneInstall(string root)
{
    public string Root { get; } = root;

    /// <summary>The X-Plane folder: the one given, else the first listed in X-Plane's own install file, else C:\X-Plane 12.</summary>
    public static XPlaneInstall? Find(string? root = null)
    {
        foreach (var candidate in Candidates(root))
        {
            if (Directory.Exists(Path.Combine(candidate, "Resources")) || Directory.Exists(Path.Combine(candidate, "Custom Scenery")))
            {
                return new XPlaneInstall(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates(string? root)
    {
        if (!string.IsNullOrWhiteSpace(root))
        {
            yield return root;
            yield break;
        }

        // X-Plane's installer lists every install here, one path per line.
        var listing = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "x-plane_install_12.txt");
        if (File.Exists(listing))
        {
            foreach (var line in File.ReadAllLines(listing))
            {
                if (line.Trim().Length > 0)
                {
                    yield return line.Trim();
                }
            }
        }

        yield return @"C:\X-Plane 12";
    }

    /// <summary>
    /// apt.dat files in the order X-Plane uses them: enabled packs from Custom Scenery/scenery_packs.ini top to
    /// bottom, with Global Airports where the *GLOBAL_AIRPORTS* line is (or last if it is missing).
    /// </summary>
    public List<string> AirportFiles()
    {
        var files = new List<string>();
        var global = Path.Combine(Root, "Global Scenery", "Global Airports", "Earth nav data", "apt.dat");
        var ini = Path.Combine(Root, "Custom Scenery", "scenery_packs.ini");
        var sawGlobal = false;
        if (File.Exists(ini))
        {
            foreach (var pack in ScenerySequence(File.ReadAllLines(ini)))
            {
                if (pack == "*GLOBAL_AIRPORTS*")
                {
                    sawGlobal = true;
                    AddIfExists(files, global);
                    continue;
                }

                var folder = Path.IsPathRooted(pack) ? pack : Path.Combine(Root, pack);
                AddIfExists(files, Path.Combine(folder, "Earth nav data", "apt.dat"));
            }
        }

        if (!sawGlobal)
        {
            AddIfExists(files, global);
        }

        return files;
    }

    /// <summary>The enabled SCENERY_PACK entries, in order, with path separators normalised.</summary>
    public static IEnumerable<string> ScenerySequence(IEnumerable<string> iniLines)
    {
        const string Prefix = "SCENERY_PACK ";
        foreach (var raw in iniLines)
        {
            var line = raw.Trim();
            if (!line.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var pack = line[Prefix.Length..].Trim().TrimEnd('/', '\\');
            yield return pack == "*GLOBAL_AIRPORTS*" ? pack : pack.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        }
    }

    public string? NavDataFile(string name) => FirstExisting(Path.Combine(Root, "Custom Data", name), Path.Combine(Root, "Resources", "default data", name));

    public string? CifpFile(string icao) =>
        FirstExisting(Path.Combine(Root, "Custom Data", "CIFP", $"{icao}.dat"), Path.Combine(Root, "Resources", "default data", "CIFP", $"{icao}.dat"));

    private static string? FirstExisting(params string[] paths) => paths.FirstOrDefault(File.Exists);

    private static void AddIfExists(List<string> files, string path)
    {
        if (File.Exists(path) && !files.Contains(path))
        {
            files.Add(path);
        }
    }
}

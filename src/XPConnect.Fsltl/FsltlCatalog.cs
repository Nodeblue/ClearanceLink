using System.Text;
using System.Xml.Linq;

namespace XPConnect.Fsltl;

/// <summary>One FSLTL livery: an <c>[FLTSIM.n]</c> entry of an aircraft.cfg, with its model and texture folders resolved.</summary>
/// <param name="Title">The MSFS title BeyondATC asks for, e.g. "FSLTL A20N BAW British Airways".</param>
/// <param name="Key">One word, unique in the package: the CSL livery name (XPMP2 splits xsb_aircraft.txt on spaces).</param>
/// <param name="ModelFiles">The model's glTF files, most detailed first (the XML's LODs).</param>
/// <param name="TextureFolders">Where the model's textures are looked up, in order (the livery's own folder, then fallbacks).</param>
public sealed record FsltlLivery(
    string Title,
    string Key,
    string Folder,
    string IcaoType,
    string IcaoAirline,
    string ModelXml,
    IReadOnlyList<string> ModelFiles,
    IReadOnlyList<string> TextureFolders)
{
    /// <summary>Liveries with the same model XML share one converted model.</summary>
    public string ModelId => ModelXml;
}

/// <summary>Reads an FSLTL package (or any MSFS SimObjects/Airplanes folder) into liveries. Read-only.</summary>
public static class FsltlCatalog
{
    public const string PackageName = "fsltl-traffic-base";

    /// <summary>Liveries of every aircraft folder under <paramref name="packageRoot"/>/SimObjects/Airplanes.</summary>
    /// <param name="problems">Gets one line per livery that could not be resolved.</param>
    /// <param name="skipFolder">Aircraft folders (by name) not to read at all.</param>
    public static List<FsltlLivery> Load(string packageRoot, List<string>? problems = null, Func<string, bool>? skipFolder = null)
    {
        var airplanes = FileLookup.Child(FileLookup.Child(packageRoot, "SimObjects") ?? "", "Airplanes")
            ?? throw new DirectoryNotFoundException($"No SimObjects/Airplanes in {packageRoot}");
        var liveries = new List<FsltlLivery>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Directory.GetDirectories(airplanes).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (skipFolder?.Invoke(Path.GetFileName(folder)) == true) continue;
            foreach (var livery in LoadFolder(folder, problems))
            {
                if (keys.Add(livery.Key)) liveries.Add(livery);
                else problems?.Add($"{livery.Title}: duplicate key {livery.Key}");
            }
        }

        return liveries;
    }

    public static IEnumerable<FsltlLivery> LoadFolder(string folder, List<string>? problems = null)
    {
        var cfgPath = FileLookup.Child(folder, "aircraft.cfg");
        if (cfgPath is null) yield break;
        var cfg = CfgFile.Load(cfgPath);

        var baseFolder = cfg.Get("VARIATION", "base_container") is { Length: > 0 } container
            ? FileLookup.Resolve(folder, container)
            : null;
        var baseCfg = baseFolder is not null && FileLookup.Child(baseFolder, "aircraft.cfg") is { } basePath ? CfgFile.Load(basePath) : null;
        var icaoType = cfg.Get("GENERAL", "icao_type_designator") ?? baseCfg?.Get("GENERAL", "icao_type_designator") ?? "";

        var entries = cfg.Sections
            .Where(s => s.StartsWith("FLTSIM.", StringComparison.OrdinalIgnoreCase))
            .Select(s => (Section: s, Index: int.TryParse(s.AsSpan(7), out var n) ? n : 0))
            .OrderBy(e => e.Index)
            .ToList();
        foreach (var (section, index) in entries)
        {
            var title = cfg.Get(section, "title");
            if (string.IsNullOrWhiteSpace(title)) continue;

            var model = ResolveModel(folder, baseFolder, cfg.Get(section, "model") ?? "");
            if (model is null)
            {
                problems?.Add($"{title}: model folder or XML not found");
                continue;
            }

            var key = Key(Path.GetFileName(folder), entries.Count > 1 ? index : null);
            var textures = TextureFolders(folder, baseFolder, cfg.Get(section, "texture") ?? "");
            var airline = cfg.Get(section, "icao_airline") ?? "";
            yield return new FsltlLivery(title, key, folder, icaoType.ToUpperInvariant(), airline.ToUpperInvariant(), model.Value.Xml, model.Value.Files, textures);
        }
    }

    /// <summary>The folder name made safe as one xsb_aircraft.txt word.</summary>
    public static string Key(string folderName, int? index)
    {
        var sb = new StringBuilder(folderName.Length + 3);
        foreach (var c in folderName) sb.Append(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        if (index is { } n) sb.Append('_').Append(n);
        return sb.ToString();
    }

    private static (string Xml, List<string> Files)? ResolveModel(string folder, string? baseFolder, string model)
    {
        var name = model.Length == 0 ? "model" : "model." + model;
        foreach (var owner in new[] { folder, baseFolder })
        {
            if (owner is null || FileLookup.Child(owner, name) is not { } dir) continue;
            if (ModelXml(dir) is { } xml && ModelFiles(xml) is { Count: > 0 } files) return (xml, files);
        }

        return null;
    }

    private static string? ModelXml(string modelDir)
    {
        if (FileLookup.Child(modelDir, "model.cfg") is { } cfgPath
            && CfgFile.Load(cfgPath).Get("models", "normal") is { Length: > 0 } normal
            && FileLookup.Resolve(modelDir, normal) is { } xml && File.Exists(xml))
        {
            return xml;
        }

        var xmls = Directory.GetFiles(modelDir, "*.xml");
        return xmls.Length == 1 ? xmls[0] : null;
    }

    /// <summary>The XML's LOD model files in the order listed (most detailed first), or the XML's namesake .gltf.</summary>
    public static List<string> ModelFiles(string xmlPath)
    {
        var dir = Path.GetDirectoryName(xmlPath)!;
        var files = new List<string>();
        try
        {
            var doc = XDocument.Load(xmlPath);
            foreach (var lod in doc.Descendants().Where(e => e.Name.LocalName.Equals("LOD", StringComparison.OrdinalIgnoreCase)))
            {
                var file = lod.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("ModelFile", StringComparison.OrdinalIgnoreCase))?.Value;
                if (file is not null && FileLookup.Child(dir, file) is { } path && !files.Contains(path)) files.Add(path);
            }
        }
        catch (System.Xml.XmlException)
        {
            // Some model XMLs are not well-formed; fall back to the namesake glTF.
        }

        if (files.Count == 0 && FileLookup.Child(dir, Path.GetFileNameWithoutExtension(xmlPath) + ".gltf") is { } single) files.Add(single);
        return files;
    }

    private static List<string> TextureFolders(string folder, string? baseFolder, string texture)
    {
        var name = texture.Length == 0 ? "texture" : "texture." + texture;
        var result = new List<string>();
        void Add(string? dir)
        {
            if (dir is not null && Directory.Exists(dir) && !result.Contains(dir, StringComparer.OrdinalIgnoreCase)) result.Add(dir);
        }

        void AddWithFallbacks(string? dir)
        {
            if (dir is null) return;
            Add(dir);
            if (FileLookup.Child(dir, "texture.cfg") is not { } cfgPath) return;
            var cfg = CfgFile.Load(cfgPath);
            for (var i = 1; i < 32 && cfg.Get("fltsim", $"fallback.{i}") is { Length: > 0 } fallback; i++)
            {
                Add(FileLookup.Resolve(dir, fallback));
            }
        }

        AddWithFallbacks(FileLookup.Child(folder, name));
        if (baseFolder is not null)
        {
            AddWithFallbacks(FileLookup.Child(baseFolder, name));
            AddWithFallbacks(FileLookup.Child(baseFolder, "texture"));
        }

        return result;
    }
}

/// <summary>Case-insensitive file lookups, so MSFS paths written on Windows also resolve on Linux.</summary>
public static class FileLookup
{
    private static readonly EnumerationOptions IgnoreCase = new() { MatchCasing = MatchCasing.CaseInsensitive, MatchType = MatchType.Simple };

    /// <summary>The entry in <paramref name="dir"/> named <paramref name="name"/>, ignoring case, or null.</summary>
    public static string? Child(string dir, string name)
    {
        if (dir.Length == 0 || name.IndexOfAny(['*', '?']) >= 0 || !Directory.Exists(dir)) return null;
        // Returns the name as it is on disk, so output file names do not depend on how a .cfg spelled it.
        return Directory.EnumerateFileSystemEntries(dir, name, IgnoreCase).FirstOrDefault();
    }

    /// <summary>A relative MSFS path (either slash, "..") resolved under <paramref name="dir"/>, ignoring case; null if missing.</summary>
    public static string? Resolve(string dir, string relative)
    {
        var current = dir;
        foreach (var part in relative.Split('\\', '/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                current = Path.GetDirectoryName(current);
                if (current is null) return null;
                continue;
            }

            current = Child(current, part);
            if (current is null) return null;
        }

        return current;
    }

    /// <summary>The first folder in <paramref name="folders"/> holding <paramref name="fileName"/>.</summary>
    public static string? Find(IEnumerable<string> folders, string fileName) =>
        folders.Select(f => Child(f, fileName)).FirstOrDefault(p => p is not null && File.Exists(p));
}

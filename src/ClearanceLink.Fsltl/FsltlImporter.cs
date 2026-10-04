using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace ClearanceLink.Fsltl;

public sealed record ImportOptions
{
    /// <summary>The FSLTL package folder (…\Community\fsltl-traffic-base). Only read.</summary>
    public required string PackageRoot { get; init; }

    /// <summary>The CSL package folder to write, e.g. &lt;X-Plane&gt;/Custom Data/CSL/ClearanceLink-FSLTL.</summary>
    public required string Output { get; init; }

    /// <summary>Largest texture side written; FSLTL's 4096 liveries cost 22 MB of video memory each, 2048 a quarter.</summary>
    public int MaxTextureSize { get; init; } = 2048;

    /// <summary>Models with more triangles than this use a lower LOD, or are left out.</summary>
    public int MaxTriangles { get; init; } = 60000;

    /// <summary>Which liveries to import (all when null).</summary>
    public Func<FsltlLivery, bool>? Filter { get; init; }

    /// <summary>Convert textures again even if the output already has them.</summary>
    public bool Force { get; init; }

    public Action<string> Log { get; init; } = _ => { };
}

public sealed record ImportResult(int Liveries, int Models, int Textures, int Skipped, List<string> Problems);

/// <summary>One catalogue entry: how ClearanceLink finds the CSL livery for a title BeyondATC asks for.</summary>
/// <param name="Model">The converted model the livery is drawn with (several liveries share one).</param>
public sealed record CatalogueEntry(string Title, string Key, string IcaoType, string IcaoAirline, string Model = "");

/// <summary>
/// Converts the user's own FSLTL install into an X-Plane CSL package that XPMP2 (the ClearanceLink plugin) loads:
/// one .obj per model and texture, one DDS per livery texture, an xsb_aircraft.txt with one entry per livery,
/// and fsltl-catalogue.json mapping BeyondATC's titles to livery keys. Nothing is written outside <see cref="ImportOptions.Output"/>.
/// </summary>
public static class FsltlImporter
{
    public const string ExportName = "ClearanceLink_FSLTL";
    public const string CatalogueFile = "fsltl-catalogue.json";

    /// <summary>Models more lopsided than this (<see cref="Obj8Converter.Asymmetry"/>) are left out.</summary>
    public const float MaxAsymmetry = 0.08f;
    private const string ObjectsFolder = "objects";

    public static ImportResult Import(ImportOptions options)
    {
        var problems = new List<string>();
        var liveries = FsltlCatalog.Load(options.PackageRoot, problems, IsMicrosoftFolder)
            .Where(l => !IsMicrosoftModel(l))
            .Where(l => options.Filter?.Invoke(l) ?? true)
            .ToList();
        options.Log($"FSLTL: {liveries.Count} liveries to import from {options.PackageRoot}");

        var objects = Path.Combine(options.Output, ObjectsFolder);
        Directory.CreateDirectory(objects);
        var airplanes = Path.Combine(options.PackageRoot, "SimObjects", "Airplanes");
        var textureJobs = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);  // output name -> source
        var keepAlpha = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);       // used by a MASK/BLEND material
        var entries = new StringBuilder($"EXPORT_NAME {ExportName}\n\n");
        var catalogue = new List<CatalogueEntry>();
        var models = 0;
        var skipped = 0;
        var modelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var model in liveries.GroupBy(l => l.ModelId, StringComparer.OrdinalIgnoreCase))
        {
            var first = model.First();
            var modelName = ModelName(airplanes, model.Key);
            for (var n = 2; !modelNames.Add(modelName); n++) modelName = $"{ModelName(airplanes, model.Key)}_{n}";
            Obj8Result converted;
            try
            {
                var lods = PickLods(first.ModelFiles, options.MaxTriangles);
                if (lods.Count == 0)
                {
                    problems.Add($"{modelName}: every LOD has more than {options.MaxTriangles} triangles; left out");
                    skipped += model.Count();
                    continue;
                }

                // A lopsided model is drawn wrong (parts off where they belong); Bluebell draws those liveries instead.
                if (Obj8Converter.Asymmetry(lods[0]) is var lopsided and > MaxAsymmetry)
                {
                    problems.Add($"{modelName}: left and right sides differ by {lopsided:P0} of its span; left out (Bluebell draws it)");
                    skipped += model.Count();
                    continue;
                }

                converted = Obj8Converter.Convert(lods, AnimationRoles.Load(model.Key), AnimationRoles.EngineParts(model.Key), AnimationRoles.UsedNames(model.Key));
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or ArgumentException)
            {
                problems.Add($"{modelName}: cannot convert ({ex.Message}); left out");
                skipped += model.Count();
                continue;
            }

            if (converted.Parts.Count == 0)
            {
                problems.Add($"{modelName}: no textured geometry; left out");
                skipped += model.Count();
                continue;
            }

            problems.AddRange(converted.Warnings.Select(w => $"{modelName}: {w}"));
            models++;

            // Each part's .obj names the first livery's textures; other liveries swap them on their OBJ8 line.
            var defaults = converted.Parts.Select(p => Textures(p, first, airplanes)).ToList();
            for (var i = 0; i < converted.Parts.Count; i++)
            {
                var part = converted.Parts[i];
                var (texture, lit) = defaults[i];
                var text = part.Text.Replace("{TEXTURE}", texture?.Name ?? "missing.dds", StringComparison.Ordinal)
                    .Replace("{TEXTURE_LIT}", lit?.Name ?? "missing.dds", StringComparison.Ordinal);
                var objPath = Path.Combine(objects, ObjName(modelName, i));
                File.WriteAllText(objPath, text);
                written.Add(Path.GetFileName(objPath));
                // XPMP2 writes a copy per livery texture ("<obj name>.<texture>.xpmp2.obj") and would keep using a stale one.
                var copies = Path.GetFileNameWithoutExtension(objPath) + ".*.xpmp2.obj";
                foreach (var copy in Directory.EnumerateFiles(objects, copies)) File.Delete(copy);
            }

            foreach (var livery in model)
            {
                var lines = new StringBuilder();
                var complete = true;
                for (var i = 0; i < converted.Parts.Count; i++)
                {
                    var (texture, lit) = Textures(converted.Parts[i], livery, airplanes);
                    if (texture is null)
                    {
                        problems.Add($"{livery.Title}: texture {converted.Parts[i].BaseColor} not found");
                        complete = false;
                        break;
                    }

                    textureJobs.TryAdd(texture.Name, texture.Source);
                    if (!converted.Parts[i].Opaque) keepAlpha[texture.Name] = true;
                    if (lit is not null) textureJobs.TryAdd(lit.Name, lit.Source);
                    lines.Append(CultureInfo($"OBJ8 SOLID YES {ExportName}/{ObjectsFolder}/{ObjName(modelName, i)}"));
                    if (texture.Name != defaults[i].Texture?.Name || lit?.Name != defaults[i].Lit?.Name)
                    {
                        lines.Append(' ').Append(texture.Name);
                        if (lit is not null) lines.Append(' ').Append(lit.Name);
                    }

                    lines.Append('\n');
                }

                if (!complete)
                {
                    skipped++;
                    continue;
                }

                var type = livery.IcaoType.Length > 0 ? livery.IcaoType : "ZZZZ";
                entries.Append("OBJ8_AIRCRAFT ").Append(livery.Key).Append('\n')
                    .Append(lines)
                    .Append("LIVERY ").Append(type).Append(' ').Append(livery.IcaoAirline.Length > 0 ? livery.IcaoAirline : "-").Append(' ').Append(livery.Key).Append("\n\n");
                catalogue.Add(new CatalogueEntry(livery.Title, livery.Key, livery.IcaoType, livery.IcaoAirline, modelName));
            }

            options.Log($"FSLTL: {modelName}: {converted.Parts.Count} object(s), {converted.Parts.Sum(p => p.Triangles)} triangles, {model.Count()} liveries");
        }

        var textures = ConvertTextures(textureJobs, keepAlpha, objects, options, problems);
        if (options.Filter is null)
        {
            // Objects a previous import wrote that this one did not (a model now has fewer parts), and their XPMP2 copies.
            foreach (var obj in Directory.EnumerateFiles(objects, "*.obj"))
            {
                var name = Path.GetFileName(obj);
                var owner = name.EndsWith(".xpmp2.obj", StringComparison.OrdinalIgnoreCase) ? name[..name.IndexOf('.')] + ".obj" : name;
                if (!written.Contains(owner)) File.Delete(obj);
            }
        }

        File.WriteAllText(Path.Combine(options.Output, "xsb_aircraft.txt"), entries.ToString());
        File.WriteAllText(Path.Combine(options.Output, CatalogueFile), JsonSerializer.Serialize(catalogue, CatalogueJson.Options));
        File.WriteAllText(Path.Combine(options.Output, "README.txt"),
            "Converted by ClearanceLink from your own FSLTL install (https://fslivetrafficliveries.com/) for your own use.\n" +
            "The models and liveries belong to FSLTL and their original authors. Do not share or upload this folder.\n");
        return new ImportResult(catalogue.Count, models, textures, skipped, problems);
    }

    /// <summary>The ten FSLTL_Asobo_* aircraft are Microsoft's own MSFS models, not FSLTL's to give; they stay on Bluebell.</summary>
    public static bool IsMicrosoftModel(FsltlLivery livery) =>
        IsMicrosoftFolder(Path.GetFileName(livery.Folder))
        || IsMicrosoftFolder(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(livery.ModelXml))) ?? "");

    public static bool IsMicrosoftFolder(string folderName) => folderName.Contains("Asobo", StringComparison.OrdinalIgnoreCase);

    /// <summary>The most detailed LOD within the triangle budget, plus the least detailed one for distant traffic.</summary>
    public static List<GltfModel> PickLods(IReadOnlyList<string> files, int maxTriangles)
    {
        var models = files.Distinct(StringComparer.OrdinalIgnoreCase).Select(GltfModel.Load).ToList();
        var fitting = models.Where(m => Triangles(m) <= maxTriangles).ToList();
        if (fitting.Count == 0) return [];
        return fitting.Count == 1 || fitting[^1] == fitting[0] ? [fitting[0]] : [fitting[0], fitting[^1]];
    }

    public static int Triangles(GltfModel model) =>
        model.Nodes.Where(n => n.Mesh >= 0).Sum(n => model.Meshes[n.Mesh].Sum(p => p.Indices.Length / 3));

    private sealed record TextureRef(string Name, string Source);

    private static (TextureRef? Texture, TextureRef? Lit) Textures(Obj8Part part, FsltlLivery livery, string airplanes)
    {
        TextureRef? Find(string? image) =>
            image is not null && FileLookup.Find(livery.TextureFolders, image) is { } source
                ? new TextureRef(TextureName(airplanes, source), source)
                : null;
        return (Find(part.BaseColor), Find(part.Emissive));
    }

    /// <summary>A flat, readable, unique file name for a source texture: its folders under Airplanes plus its name.</summary>
    public static string TextureName(string airplanes, string source)
    {
        var relative = Path.GetRelativePath(airplanes, source);
        var name = relative.EndsWith(".PNG.DDS", StringComparison.OrdinalIgnoreCase) ? relative[..^8] : Path.ChangeExtension(relative, null);
        return Safe(name) + ".dds";
    }

    /// <summary>The aircraft folder plus the model XML's name: one model folder can hold several XMLs (e.g. engine variants).</summary>
    public static string ModelName(string airplanes, string xml)
    {
        var folder = Path.GetRelativePath(airplanes, Path.GetDirectoryName(xml)!).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return Safe(folder + "_" + Path.GetFileNameWithoutExtension(xml));
    }

    private static string ObjName(string modelName, int part) => $"{modelName}_{part}.obj";

    private static string Safe(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text) sb.Append(char.IsAsciiLetterOrDigit(c) || c is '-' ? c : '_');
        return sb.ToString();
    }

    private static string CultureInfo(FormattableString s) => FormattableString.Invariant(s);

    private static int ConvertTextures(ConcurrentDictionary<string, string> jobs, ConcurrentDictionary<string, bool> keepAlpha, string folder, ImportOptions options, List<string> problems)
    {
        var failures = new ConcurrentBag<string>();
        var done = 0;
        var todo = jobs.Where(j => options.Force || !File.Exists(Path.Combine(folder, j.Key))).ToList();
        options.Log($"FSLTL: converting {todo.Count} textures ({jobs.Count - todo.Count} already done), at most {options.MaxTextureSize} px");
        Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, job =>
        {
            try
            {
                var bytes = DdsTexture.Load(job.Value).ToXPlane(options.MaxTextureSize, opaque: !keepAlpha.ContainsKey(job.Key));
                var target = Path.Combine(folder, job.Key);
                File.WriteAllBytes(target + ".tmp", bytes);
                File.Move(target + ".tmp", target, overwrite: true);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException or ArgumentException)
            {
                failures.Add($"{job.Value}: {ex.Message}");
            }

            var n = Interlocked.Increment(ref done);
            if (n % 200 == 0) options.Log($"FSLTL: {n}/{todo.Count} textures");
        });
        problems.AddRange(failures);
        return jobs.Count - failures.Count;
    }

    /// <summary>The catalogue written by an import, or empty if there is none, it is damaged or it is being written.</summary>
    public static List<CatalogueEntry> LoadCatalogue(string packageFolder)
    {
        var path = Path.Combine(packageFolder, CatalogueFile);
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<CatalogueEntry>>(File.ReadAllText(path), CatalogueJson.Options) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

internal static class CatalogueJson
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

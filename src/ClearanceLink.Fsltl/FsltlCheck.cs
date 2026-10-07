namespace ClearanceLink.Fsltl;

/// <summary>What a chosen FSLTL folder holds, for the window's FSLTL page. Read-only.</summary>
/// <param name="Liveries">
/// Liveries (<c>[FLTSIM.n]</c> entries with a title), with or without a model; not Microsoft's own models. A livery
/// whose key repeats another's is dropped, as the importer does (FSLTL's keys come from unique folder names).
/// </param>
/// <param name="Models">Base models the liveries use (distinct model XMLs found).</param>
/// <param name="WithoutModel">Liveries whose base model is not in the folder: they can't be converted.</param>
/// <param name="Version">FSLTL's package_version, or null when its manifest.json has none.</param>
public sealed record FsltlHealth(int Liveries, int Models, int WithoutModel, string? Version)
{
    /// <summary>Liveries were found but not one base model: a damaged or half-copied FSLTL.</summary>
    public bool NoModels => Liveries > 0 && Models == 0;
}

public static class FsltlCheck
{
    /// <summary>
    /// Counts the liveries and base models in an FSLTL package. Reads every aircraft.cfg (about 2,300 small files for
    /// FSLTL 1.5), so it runs off the window's thread. Null when the folder can't be read at all.
    /// </summary>
    public static FsltlHealth? Check(string packageRoot)
    {
        try
        {
            // Microsoft's own models (FSLTL_Asobo_*) are never converted, so they aren't counted either.
            var problems = new List<string>();
            var liveries = FsltlCatalog.Load(packageRoot, problems, FsltlImporter.IsMicrosoftFolder)
                .Where(l => !FsltlImporter.IsMicrosoftModel(l))
                .ToList();
            var withoutModel = problems.Count(p => p.EndsWith(FsltlCatalog.NoModelProblem, StringComparison.Ordinal));
            var models = liveries.Select(l => l.ModelId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            return new FsltlHealth(liveries.Count + withoutModel, models, withoutModel, FsltlImporter.PackageVersion(packageRoot));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>One log line for the support file: "fsltl-check: ...".</summary>
    public static string Describe(string packageRoot, FsltlHealth? health) => health switch
    {
        null => $"fsltl-check: {packageRoot}: could not be read",
        _ => $"fsltl-check: {packageRoot}: version {health.Version ?? "unknown"}, {health.Liveries} liveries, {health.Models} base models, "
            + $"{health.WithoutModel} liveries without a model{(health.NoModels ? "; no base models, so nothing can be converted" : "")}",
    };
}

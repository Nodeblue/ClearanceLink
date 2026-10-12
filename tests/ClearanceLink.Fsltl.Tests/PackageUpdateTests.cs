namespace ClearanceLink.Fsltl.Tests;

/// <summary>Update detection, progress, stopping, atomic and stale files, texture sizes and the folder picker.</summary>
public sealed class PackageUpdateTests
{
    private static ImportOptions Options(FakePackage package, string output, int size = 4) =>
        new() { PackageRoot = package.Root, Output = output, MaxTextureSize = size };

    [Fact]
    public void Import_records_what_the_package_was_made_from_and_knows_when_it_is_out_of_date()
    {
        using var package = new FakePackage();
        var output = Path.Combine(package.Root, "out");
        Assert.Null(FsltlImporter.OutOfDate(package.Root, output)); // nothing converted yet: nothing to update

        FsltlImporter.Import(Options(package, output));

        var manifest = FsltlImporter.LoadManifest(output)!;
        Assert.Equal(FsltlImporter.ImporterVersion, manifest.ImporterVersion);
        Assert.Equal(4, manifest.TextureSize);
        Assert.False(manifest.Partial);
        Assert.StartsWith("files 2 ", manifest.FsltlVersion, StringComparison.Ordinal); // no manifest.json: two aircraft.cfg files
        Assert.Null(FsltlImporter.OutOfDate(package.Root, output));

        // FSLTL updated: its manifest.json now has a version.
        File.WriteAllText(Path.Combine(package.Root, "manifest.json"), """{ "package_version": "2.1.0", "title": "FSLTL" }""");
        Assert.Equal("2.1.0", FsltlImporter.FsltlVersion(package.Root));
        Assert.Equal("FSLTL was updated", FsltlImporter.OutOfDate(package.Root, output));
        FsltlImporter.Import(Options(package, output));
        Assert.Null(FsltlImporter.OutOfDate(package.Root, output));

        // A package converted before manifests were written, or by a version that converts differently.
        File.WriteAllText(Path.Combine(output, FsltlImporter.ManifestFile), """{ "fsltlVersion": "2.1.0", "importerVersion": 0, "textureSize": 4 }""");
        Assert.NotNull(FsltlImporter.OutOfDate(package.Root, output));
        File.Delete(Path.Combine(output, FsltlImporter.ManifestFile));
        Assert.NotNull(FsltlImporter.OutOfDate(package.Root, output));

        // Only some liveries imported.
        FsltlImporter.Import(Options(package, output) with { Filter = l => l.IcaoAirline == "BAW" });
        Assert.True(FsltlImporter.LoadManifest(output)!.Partial);
        Assert.NotNull(FsltlImporter.OutOfDate(package.Root, output));
    }

    [Fact]
    public void Fingerprint_changes_when_an_aircraft_is_added()
    {
        using var package = new FakePackage();
        var before = FsltlImporter.FsltlVersion(package.Root);
        var added = Directory.CreateDirectory(Path.Combine(package.Root, "SimObjects", "Airplanes", "FSLTL_B738")).FullName;
        File.WriteAllText(Path.Combine(added, "aircraft.cfg"), "[GENERAL]\n");

        Assert.NotEqual(before, FsltlImporter.FsltlVersion(package.Root));
    }

    [Fact]
    public void Progress_counts_models_then_textures()
    {
        using var package = new FakePackage();
        var seen = new List<ImportProgress>();

        FsltlImporter.Import(Options(package, Path.Combine(package.Root, "out")) with { Progress = p => { lock (seen) seen.Add(p); } });

        Assert.Equal(new ImportProgress(ImportStage.Models, 0, 1), seen[0]);
        Assert.Contains(new ImportProgress(ImportStage.Textures, 2, 2), seen);
        Assert.Equal(ImportStage.Saving, seen[^1].Stage);
    }

    [Fact]
    public void A_stopped_import_leaves_the_package_from_before_whole()
    {
        using var package = new FakePackage();
        var output = Path.Combine(package.Root, "out");
        FsltlImporter.Import(Options(package, output));
        var files = Snapshot(output);

        // Stopped once the models are done, as the textures start: nothing of the new run may show.
        using var cancel = new CancellationTokenSource();
        var options = Options(package, output, size: 8) with
        {
            Force = true,
            Cancel = cancel.Token,
            Progress = p =>
            {
                if (p.Stage == ImportStage.Textures) cancel.Cancel();
            },
        };
        Assert.ThrowsAny<OperationCanceledException>(() => FsltlImporter.Import(options));

        Assert.Equal(files, Snapshot(output));
        Assert.Equal(4, DdsTexture.Load(Path.Combine(output, "objects", "FSLTL_A20N_TEXTURE_LIVERY_T.dds")).Width);
    }

    [Fact]
    public void A_full_import_removes_textures_no_livery_uses_and_leftover_temporary_files()
    {
        using var package = new FakePackage();
        var output = Path.Combine(package.Root, "out");
        var objects = Directory.CreateDirectory(Path.Combine(output, "objects")).FullName;
        foreach (var name in new[] { "Renamed_Livery.dds", "FSLTL_A20N_TEXTURE_LIVERY_T.dds.tmp", "Old_0.obj.tmp" })
        {
            File.WriteAllText(Path.Combine(objects, name), "x");
        }

        File.WriteAllText(Path.Combine(output, "xsb_aircraft.txt.tmp"), "x");

        // A filtered import knows only some liveries' textures: it keeps the others.
        FsltlImporter.Import(Options(package, output) with { Filter = l => l.IcaoAirline == "BAW" });
        Assert.True(File.Exists(Path.Combine(objects, "Renamed_Livery.dds")));

        FsltlImporter.Import(Options(package, output));
        Assert.Equal(["FSLTL_A20N_A320_0.obj", "FSLTL_A20N_A320_lights.obj", "FSLTL_A20N_BAW_Speedbird_TEXTURE_BAW_LIVERY_T.dds", "FSLTL_A20N_TEXTURE_LIVERY_T.dds"],
            Directory.GetFiles(objects).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Empty(Directory.GetFiles(output, "*.tmp"));
    }

    [Fact]
    public void Textures_converted_at_another_size_are_converted_again()
    {
        using var package = new FakePackage();
        var output = Path.Combine(package.Root, "out");
        var texture = Path.Combine(output, "objects", "FSLTL_A20N_TEXTURE_LIVERY_T.dds");
        FsltlImporter.Import(Options(package, output, size: 4));
        Assert.Equal(4, DdsTexture.Load(texture).Width);

        var log = new List<string>();
        FsltlImporter.Import(Options(package, output, size: 8) with { Log = log.Add });
        Assert.Equal(8, DdsTexture.Load(texture).Width);
        Assert.Contains("FSLTL: converting 2 textures (0 already done), at most 8 px", log);

        log.Clear();
        FsltlImporter.Import(Options(package, output, size: 8) with { Log = log.Add });
        Assert.Contains("FSLTL: converting 0 textures (2 already done), at most 8 px", log);
    }

    [Fact]
    public void Loading_for_a_size_reads_only_the_levels_it_needs_and_converts_the_same()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cl-dds-{Guid.NewGuid():N}.dds");
        File.WriteAllBytes(path, DdsBuilder.Dxt5(16, 16));
        try
        {
            var trimmed = DdsTexture.Load(path, 4);
            Assert.Equal(5, trimmed.Mips.Count);
            Assert.Empty(trimmed.Mips[0]);
            Assert.Empty(trimmed.Mips[1]);
            Assert.Equal(DdsTexture.Load(path).ToXPlane(4, opaque: true), trimmed.ToXPlane(4, opaque: true));
            Assert.Equal((DdsFormat.Dxt5, 16, 16), DdsTexture.ReadHeader(path));
            Assert.Equal((4, 4), DdsTexture.TargetSize(16, 16, 4));
            Assert.Equal((2048, 1024), DdsTexture.TargetSize(4096, 2048, 2048));
            Assert.Equal((512, 512), DdsTexture.TargetSize(512, 512, 2048));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Folder_picker_accepts_the_community_folder_and_folders_inside_the_package()
    {
        var root = Path.Combine(Path.GetTempPath(), "cl-locate-" + Guid.NewGuid().ToString("N"));
        var community = Path.Combine(root, "Packages", "Community");
        var fsltl = Path.Combine(community, "fsltl-traffic-base");
        var aircraft = Directory.CreateDirectory(Path.Combine(fsltl, "SimObjects", "Airplanes", "FSLTL_A20N")).FullName;
        Directory.CreateDirectory(Path.Combine(community, "other-package"));
        try
        {
            Assert.Equal(fsltl, FsltlLocator.Find(fsltl));
            Assert.Equal(fsltl, FsltlLocator.Find(fsltl + Path.DirectorySeparatorChar));
            Assert.Equal(fsltl, FsltlLocator.Find(community));
            Assert.Equal(fsltl, FsltlLocator.Find(Path.Combine(root, "Packages")));
            Assert.Equal(fsltl, FsltlLocator.Find(aircraft));
            Assert.Equal(fsltl, FsltlLocator.Find(Path.Combine(fsltl, "SimObjects")));
            Assert.Null(FsltlLocator.Find(Path.Combine(community, "other-package")));
            Assert.Null(FsltlLocator.Find(root));

            // Found through UserCfg.opt's NextBoot folder too (issue #29), after the folder the ATC app reads.
            var cfg = Path.Combine(root, "UserCfg.opt");
            File.WriteAllText(cfg, $"InstalledPackagesPathNextBoot \"{Path.Combine(root, "Packages")}\"\nInstalledPackagesPath \"{Path.Combine(root, "Empty")}\"\n");
            Assert.Equal(fsltl, FsltlLocator.Find(userCfgs: [cfg]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Msfs_counts_as_installed_when_it_wrote_its_settings()
    {
        var cfg = Path.GetTempFileName();
        try
        {
            Assert.True(FsltlLocator.MsfsInstalled([Path.Combine(Path.GetTempPath(), "no-such", "UserCfg.opt"), cfg]));
            Assert.False(FsltlLocator.MsfsInstalled([Path.Combine(Path.GetTempPath(), "no-such", "UserCfg.opt")]));
        }
        finally
        {
            File.Delete(cfg);
        }
    }

    [Theory]
    [InlineData(null, true, 2048)]
    [InlineData("1024", true, 1024)]
    [InlineData("4096", true, 4096)]
    [InlineData("2k", false, 0)]
    [InlineData("1000", false, 0)]
    [InlineData("-2048", false, 0)]
    [InlineData("65536", false, 0)]
    public void Texture_size_option_is_read_without_throwing(string? text, bool ok, int size)
    {
        Assert.Equal(ok, FsltlImporter.TryParseTextureSize(text, out var parsed));
        if (ok) Assert.Equal(size, parsed);
    }

    private static Dictionary<string, string> Snapshot(string folder) => Directory
        .GetFiles(folder, "*", SearchOption.AllDirectories)
        .ToDictionary(f => Path.GetRelativePath(folder, f), f => Convert.ToBase64String(File.ReadAllBytes(f)));
}

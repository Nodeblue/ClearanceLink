using ClearanceLink.Fsltl;

namespace ClearanceLink.Host.Tests;

public sealed class BatcTrafficModelsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("clearancelink-batc-models").FullName;

    private string Store => Path.Combine(_root, "Local", "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt");

    private string Steam => Path.Combine(_root, "Roaming", "Microsoft Flight Simulator 2024", "UserCfg.opt");

    private BatcTrafficModels Models => new(Store, Steam, Path.Combine(_root, "ClearanceLink", "BeyondATC packages"));

    public void Dispose()
    {
        // Windows can't delete a junction recursively: remove the links first (the folders they point at stay).
        foreach (var link in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories)
            .Select(d => new DirectoryInfo(d)).Where(d => d.LinkTarget is not null).ToList())
        {
            link.Delete();
        }

        Directory.Delete(_root, recursive: true);
    }

    /// <summary>A FSLTL package as installed: SimObjects\Airplanes\...\aircraft.cfg.</summary>
    private static string Fsltl(string folder)
    {
        var aircraft = Path.Combine(folder, "SimObjects", "Airplanes", "FSLTL_A320_SAS");
        Directory.CreateDirectory(aircraft);
        File.WriteAllText(Path.Combine(aircraft, "aircraft.cfg"), "[FLTSIM.0]\ntitle=FSLTL A320 SAS\n");
        return folder;
    }

    /// <summary>BeyondATC's own lookup (TrafficModelManager.FindCommunity then GetFiles): Store UserCfg.opt, else Steam; then each aircraft.cfg under a Community folder whose name contains fsltl-traffic-base.</summary>
    private List<string> WhatBatcFinds()
    {
        var cfg = File.Exists(Store) ? Store : File.Exists(Steam) ? Steam : null;
        if (cfg is null || FsltlLocator.InstalledPackagesPath(File.ReadAllText(cfg)) is not { } packages)
        {
            return [];
        }

        var community = Path.Combine(packages, "Community");
        return !Directory.Exists(community)
            ? []
            : Directory.GetDirectories(community)
                .Where(d => Path.GetFileName(d).Contains("fsltl-traffic-base", StringComparison.OrdinalIgnoreCase))
                .SelectMany(d => Directory.GetFiles(d, "aircraft.cfg", SearchOption.AllDirectories))
                .ToList();
    }

    private static void WriteUserCfg(string file, string packages)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, $"Version 1\r\nInstalledPackagesPath \"{packages}\"\r\n");
    }

    [Fact]
    public void Without_msfs_2024_beyondatc_is_pointed_at_a_chosen_fsltl_folder_until_close()
    {
        var fsltl = Fsltl(Path.Combine(_root, "Downloads", "fsltl-traffic-base"));
        Assert.Empty(WhatBatcFinds());

        var result = Models.Provide(fsltl);

        Assert.Equal(BatcTrafficModels.Outcome.Pointed, result.Outcome);
        Assert.Single(WhatBatcFinds());
        Assert.False(FsltlLocator.MsfsInstalled([Store, Steam]));
        Assert.Null(FsltlLocator.Find(userCfgs: [Store, Steam]));

        Assert.NotNull(Models.Remove());

        Assert.Empty(WhatBatcFinds());
        Assert.False(Directory.Exists(Path.GetDirectoryName(Steam)));
        Assert.True(File.Exists(Path.Combine(fsltl, "SimObjects", "Airplanes", "FSLTL_A320_SAS", "aircraft.cfg")));
    }

    [Fact]
    public void Fsltl_in_an_msfs_2020_community_folder_is_named_directly()
    {
        var packages = Path.Combine(_root, "MSFS2020", "Packages");
        Fsltl(Path.Combine(packages, "Community", "fsltl-traffic-base"));

        Assert.Equal(BatcTrafficModels.Outcome.Pointed, Models.Provide(Path.Combine(packages, "Community", "fsltl-traffic-base")).Outcome);

        Assert.Contains($"\"{packages}\"", File.ReadAllText(Steam));
        Assert.Single(WhatBatcFinds());
        Assert.False(Directory.Exists(Path.Combine(_root, "ClearanceLink")));
    }

    [Fact]
    public void Pointing_again_at_another_folder_replaces_the_link()
    {
        Models.Provide(Fsltl(Path.Combine(_root, "Old", "fsltl-traffic-base")));
        var now = Fsltl(Path.Combine(_root, "New", "fsltl-traffic-base"));
        File.WriteAllText(Path.Combine(now, "SimObjects", "Airplanes", "FSLTL_A320_SAS", "aircraft.cfg"), "new");

        Assert.Equal(BatcTrafficModels.Outcome.Pointed, Models.Provide(now).Outcome);
        Assert.Equal(BatcTrafficModels.Outcome.Pointed, Models.Provide(now).Outcome);

        Assert.Equal("new", File.ReadAllText(Assert.Single(WhatBatcFinds())));
        Assert.True(Directory.Exists(Path.Combine(_root, "Old", "fsltl-traffic-base", "SimObjects")));
    }

    [Fact]
    public void A_real_msfs_2024_with_fsltl_is_left_alone()
    {
        var packages = Path.Combine(_root, "MSFS2024", "Packages");
        Fsltl(Path.Combine(packages, "Community", "fsltl-traffic-base"));
        WriteUserCfg(Store, packages);
        var before = File.ReadAllText(Store);

        var result = Models.Provide(Path.Combine(packages, "Community", "fsltl-traffic-base"));

        Assert.Equal(BatcTrafficModels.Outcome.Found, result.Outcome);
        Assert.False(File.Exists(Steam));
        Assert.Null(Models.Remove());
        Assert.Equal(before, File.ReadAllText(Store));
    }

    [Fact]
    public void A_real_msfs_2024_without_traffic_models_is_reported_not_changed()
    {
        var packages = Path.Combine(_root, "MSFS2024", "Packages");
        Directory.CreateDirectory(Path.Combine(packages, "Community", "some-scenery"));
        WriteUserCfg(Steam, packages);
        var fsltl = Fsltl(Path.Combine(_root, "Downloads", "fsltl-traffic-base"));

        var result = Models.Provide(fsltl);

        Assert.Equal(BatcTrafficModels.Outcome.NotInMsfs2024, result.Outcome);
        Assert.Contains("only in MSFS 2024", result.Line);
        Assert.Null(Models.Remove());
        Assert.Contains(packages, File.ReadAllText(Steam));
    }

    [Fact]
    public void Aig_models_count_as_traffic_models_like_beyondatc_does()
    {
        var community = Path.Combine(_root, "Community");
        Directory.CreateDirectory(Path.Combine(community, "aig-aitraffic-oci-beta"));

        Assert.True(BatcTrafficModels.HasTrafficModels(community));
        Assert.False(BatcTrafficModels.HasTrafficModels(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void No_fsltl_folder_removes_a_stale_file_and_says_so()
    {
        Models.Provide(Fsltl(Path.Combine(_root, "Downloads", "fsltl-traffic-base")));

        var result = Models.Provide(null);

        Assert.Equal(BatcTrafficModels.Outcome.NoFsltl, result.Outcome);
        Assert.False(File.Exists(Steam));
    }

    [Fact]
    public void A_stand_in_from_another_running_clearancelink_is_left_to_it()
    {
        var fsltl = Fsltl(Path.Combine(_root, "Downloads", "fsltl-traffic-base"));
        var other = new BatcTrafficModels(Store, Steam, Path.Combine(_root, "ClearanceLink", "BeyondATC packages"), ownerPid: 1111, isAlive: _ => true);
        var me = new BatcTrafficModels(Store, Steam, Path.Combine(_root, "ClearanceLink", "BeyondATC packages"), ownerPid: 2222, isAlive: _ => true);
        Assert.Equal(BatcTrafficModels.Outcome.Pointed, other.Provide(fsltl).Outcome);

        Assert.Equal(BatcTrafficModels.Outcome.PointedByAnother, me.Provide(fsltl).Outcome);
        Assert.Null(me.Remove());
        Assert.Single(WhatBatcFinds());

        Assert.NotNull(other.Remove());
        Assert.Empty(WhatBatcFinds());
    }

    [Fact]
    public void A_stand_in_whose_clearancelink_has_gone_is_removed()
    {
        var fsltl = Fsltl(Path.Combine(_root, "Downloads", "fsltl-traffic-base"));
        new BatcTrafficModels(Store, Steam, Path.Combine(_root, "ClearanceLink", "BeyondATC packages"), ownerPid: 1111).Provide(fsltl);
        var guard = new BatcTrafficModels(Store, Steam, Path.Combine(_root, "ClearanceLink", "BeyondATC packages"), ownerPid: 2222, isAlive: _ => false);

        Assert.NotNull(guard.Remove());
        Assert.False(File.Exists(Steam));
    }

    [Fact]
    public void A_stale_stand_in_goes_once_msfs_2024_has_its_own_file()
    {
        Models.Provide(Fsltl(Path.Combine(_root, "Downloads", "fsltl-traffic-base")));
        var packages = Path.Combine(_root, "MSFS2024", "Packages");
        Fsltl(Path.Combine(packages, "Community", "fsltl-traffic-base"));
        WriteUserCfg(Store, packages);

        Assert.Equal(BatcTrafficModels.Outcome.Found, Models.Provide(null).Outcome);
        Assert.False(File.Exists(Steam));
        Assert.True(File.Exists(Store));
    }

    [Fact]
    public void An_msfs_folder_that_was_already_there_is_kept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Steam)!);
        Models.Provide(Fsltl(Path.Combine(_root, "Downloads", "fsltl-traffic-base")));
        Models.Provide(Fsltl(Path.Combine(_root, "Downloads", "fsltl-traffic-base")));

        Assert.NotNull(Models.Remove());

        Assert.True(Directory.Exists(Path.GetDirectoryName(Steam)));
    }

    [Fact]
    public void A_folder_name_cmd_would_expand_is_refused_not_linked()
    {
        var result = Models.Provide(Fsltl(Path.Combine(_root, "50%off", "fsltl-traffic-base")));

        Assert.Equal(BatcTrafficModels.Outcome.Failed, result.Outcome);
        Assert.False(File.Exists(Steam));
    }

    [Fact]
    public void A_real_folder_where_the_link_goes_is_never_emptied()
    {
        var real = Fsltl(Path.Combine(_root, "ClearanceLink", "BeyondATC packages", "Community", "fsltl-traffic-base"));

        Assert.Equal(BatcTrafficModels.Outcome.Failed, Models.Provide(Fsltl(Path.Combine(_root, "Downloads", "fsltl-traffic-base"))).Outcome);

        Assert.True(File.Exists(Path.Combine(real, "SimObjects", "Airplanes", "FSLTL_A320_SAS", "aircraft.cfg")));
    }

    [Fact]
    public void Check_predicts_what_beyondatc_finds_without_writing()
    {
        Assert.Equal(BatcTrafficModels.Outcome.NoFsltl, Models.Check(null));
        var fsltl = Fsltl(Path.Combine(_root, "Downloads", "fsltl-traffic-base"));
        Assert.Equal(BatcTrafficModels.Outcome.Pointed, Models.Check(fsltl));
        Assert.False(File.Exists(Steam));

        var packages = Path.Combine(_root, "MSFS2024", "Packages");
        Directory.CreateDirectory(Path.Combine(packages, "Community"));
        WriteUserCfg(Store, packages);
        Assert.Equal(BatcTrafficModels.Outcome.NotInMsfs2024, Models.Check(fsltl));
        Fsltl(Path.Combine(packages, "Community", "fsltl-traffic-base"));
        Assert.Equal(BatcTrafficModels.Outcome.Found, Models.Check(null));
    }
}

using System.Globalization;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;

namespace ClearanceLink.Fsltl.Tests;

public sealed class FsltlTests
{
    [Fact]
    public void Cfg_values_drop_quotes_and_comments_and_ignore_case()
    {
        var cfg = CfgFile.Parse("[FLTSIM.0]\r\ntitle = \"FSLTL A20N; BAW\" ; comment\r\nModel=BAW ; x\r\n");

        Assert.Equal("FSLTL A20N; BAW", cfg.Get("fltsim.0", "TITLE"));
        Assert.Equal("BAW", cfg.Get("FLTSIM.0", "model"));
        Assert.Null(cfg.Get("FLTSIM.0", "texture"));
    }

    [Fact]
    public void Catalog_resolves_base_container_model_and_texture_fallbacks()
    {
        using var package = new FakePackage();

        var problems = new List<string>();
        var liveries = FsltlCatalog.Load(package.Root, problems);

        Assert.Empty(problems);
        Assert.Equal(2, liveries.Count);
        var baw = Assert.Single(liveries, l => l.Title == "FSLTL_A20N_BAW-British Airways");
        Assert.Equal("FSLTL_A20N_BAW_Speedbird", baw.Key); // one word for xsb_aircraft.txt
        Assert.Equal("A20N", baw.IcaoType);                // from the base container
        Assert.Equal("BAW", baw.IcaoAirline);
        Assert.EndsWith("A320.xml", baw.ModelXml);
        Assert.EndsWith("A320_LOD0.gltf", Assert.Single(baw.ModelFiles));
        Assert.Equal(["TEXTURE.BAW", "TEXTURE"], baw.TextureFolders.Select(Path.GetFileName));
        Assert.Equal(liveries[0].ModelId, baw.ModelId); // shares the base model
    }

    [Fact]
    public void Locator_reads_installed_packages_path()
    {
        Assert.Equal(@"D:\MSFS\Packages", FsltlLocator.InstalledPackagesPath("Version 1\nInstalledPackagesPath \"D:\\MSFS\\Packages\"\n"));
        Assert.Null(FsltlLocator.InstalledPackagesPath("Version 1\n"));
    }

    [Fact]
    public void Winding_is_read_from_the_vertex_normals()
    {
        float[] positions = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        var facingUs = new GltfPrimitive(positions, [0, 0, 1, 0, 0, 1, 0, 0, 1], new float[6], [0, 1, 2], 0);
        var facingAway = facingUs with { Normals = [0, 0, -1, 0, 0, -1, 0, 0, -1] };

        Assert.True(Obj8Converter.IsCounterClockwise(facingUs));   // glTF standard: reversed for X-Plane
        Assert.False(Obj8Converter.IsCounterClockwise(facingAway)); // Asobo's clockwise export: kept as is
    }

    [Fact]
    public void Gltf_decodes_asobo_half_float_uvs_and_byte_normals()
    {
        using var package = new FakePackage();
        var model = GltfModel.Load(FsltlCatalog.Load(package.Root)[0].ModelFiles[0]);

        var p = Assert.Single(model.Meshes[0]);
        Assert.Equal([0f, 0f, 1f, 0f, 0f, 1f], p.Uvs);
        Assert.Equal(1f, p.Normals[2]);
        Assert.Equal([0, 1, 2], p.Indices);
        Assert.Equal("LIVERY_T.PNG.DDS", model.Materials[0].BaseColor);
        Assert.Equal("custom_anim_GEAR_00", model.Animations[0].Name);
    }

    [Fact]
    public void Still_geometry_is_turned_to_xplane_axes_and_wound_clockwise()
    {
        using var package = new FakePackage();
        var model = GltfModel.Load(FsltlCatalog.Load(package.Root)[0].ModelFiles[0]);

        model.Animations.Clear(); // still: no gear animation
        var part = Assert.Single(Obj8Converter.Convert([model], new Dictionary<string, AnimationRole>()).Parts);

        // Node at (1, 2, 3); a half turn about Y maps (x, y, z) to (-x, y, -z).
        var vertices = Lines(part.Text, "VT").Select(l => l.Split(' ').Skip(1).Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray()).ToList();
        Assert.Equal([-1f, 2f, -3f, 0f, 0f, -1f, 0f, 1f], vertices[0]); // t = 1 - v
        Assert.Equal([-2f, 2f, -3f, 0f, 0f, -1f, 1f, 1f], vertices[1]);
        Assert.Equal(["IDX 0", "IDX 2", "IDX 1"], Lines(part.Text, "IDX")); // counter-clockwise against its normals: reversed
        Assert.Contains("TEXTURE {TEXTURE}", part.Text);
        Assert.Contains("ATTR_no_blend 0.5", part.Text); // MASK material
        Assert.DoesNotContain("ANIM_", part.Text);
    }

    [Fact]
    public void Gear_animation_becomes_a_keyed_rotation_on_xpmp2_gear_ratio()
    {
        using var package = new FakePackage();
        var livery = FsltlCatalog.Load(package.Root)[0];
        var model = GltfModel.Load(livery.ModelFiles[0]);

        var part = Assert.Single(Obj8Converter.Convert([model], AnimationRoles.Load(livery.ModelXml)).Parts);

        var commands = Lines(part.Text, "ANIM");
        Assert.Equal("ANIM_begin", commands[0]);
        Assert.Contains("ANIM_rotate 0 -1 0 180 180 0 0 none", commands); // to X-Plane axes, outside the node
        Assert.Contains("ANIM_trans 1 2 3 1 2 3 0 0 none", commands);   // the node's own position
        Assert.Contains("ANIM_rotate_begin 1 0 0 libxplanemp/controls/gear_ratio", commands);
        Assert.Contains("ANIM_rotate_key 0 0", commands);
        Assert.Contains("ANIM_rotate_key 1 90", commands);
        Assert.Equal("ANIM_end", commands[^1]);
    }

    [Theory]
    [InlineData("ASOBO_GEAR_Left_Template", "GEARS", "", AnimationRoles.GearRatio)]
    [InlineData("ASOBO_GT_Anim_Code", "Flaps", "(A:TRAILING EDGE FLAPS LEFT PERCENT, percent)", AnimationRoles.FlapRatio)]
    [InlineData("ASOBO_GT_Anim_Code", "Slats", "(A:SIM ON GROUND, bool) (A:SPOILERS HANDLE POSITION, percent)", AnimationRoles.SlatRatio)]
    [InlineData("ASOBO_GT_Anim_Code", "SpoilerL", "(A:SPOILERS HANDLE POSITION, percent)", AnimationRoles.SpoilerRatio)]
    [InlineData("ASOBO_GT_Anim_Code", "Reverser", "(A:TURB ENG JET THRUST:1, pounds)", AnimationRoles.ReverserRatio)]
    [InlineData("FSLTL_Wheel_Roll_Main", "Wheels", "", AnimationRoles.TireRotation)]
    [InlineData("FSLTL_Steering", "Wheels", "", AnimationRoles.NoseWheelAngle)]
    [InlineData("FSLTL_Elevator_Droop", "Wheels", "", null)]
    public void Templates_pick_the_xpmp2_dataref(string template, string component, string code, string? dataref) =>
        Assert.Equal(dataref, AnimationRoles.Classify(template, component, code).Dataref);

    [Theory]
    [InlineData("main_gear_l", AnimationRoles.GearRatio)]
    [InlineData("l_tire_anim", AnimationRoles.TireRotation)]
    [InlineData("nose_gear_steering", null)]
    [InlineData("N1_still_left", AnimationRoles.EngineAngle)] // still blades turn with the engine
    public void Animations_missing_from_the_xml_are_judged_by_name(string name, string? dataref) =>
        Assert.Equal(dataref, AnimationRoles.FromName(name).Dataref);

    [Theory]
    [InlineData("prop0_blurred", AnimationRoles.EnginePart.Blur)]
    [InlineData("N1_blur_left", AnimationRoles.EnginePart.Blur)]
    [InlineData("prop1_still", AnimationRoles.EnginePart.Still)]
    [InlineData("prop0_slow", AnimationRoles.EnginePart.Blur)]
    [InlineData("N1_still_right", AnimationRoles.EnginePart.Still)]
    [InlineData("l_tire_blurred_key", null)]
    [InlineData("main_gear_l", null)]
    public void Engine_blur_discs_and_still_blades_are_found_by_name(string name, AnimationRoles.EnginePart? part) =>
        Assert.Equal(part, AnimationRoles.EnginePartFromName(name));

    [Theory]
    [InlineData(null, null, null, 500f, 1000f)]  // FSLTL_Steering defaults: P2 50, OFFSET 0, ANIM_LENGTH 1000
    [InlineData("5", "50", "200", 100f, 200f)]   // FSLTL's TFS A332: half way along its keys
    public void Steering_is_posed_straight_ahead(string? p2, string? offset, string? length, float frame, float of)
    {
        var role = AnimationRoles.Classify("FSLTL_Steering", "Wheels", "", name => name switch
        {
            "LEGACY_PARAM_2" => p2,
            "OFFSET" => offset,
            "ANIM_LENGTH" => length,
            _ => null,
        });

        Assert.Equal(AnimationRoles.NoseWheelAngle, role.Dataref);
        Assert.Equal((frame, of), (role.PoseFrame!.Value, role.PoseLength));
    }

    [Fact]
    public void Simplify_drops_keys_on_a_straight_line()
    {
        var keys = new List<(float, float[])> { (0, [0f]), (0.25f, [0f]), (0.5f, [10f]), (0.75f, [20f]), (1, [20f]) };

        Assert.Equal([0f, 0.25f, 0.75f, 1f], Obj8Converter.SimplifyKeys(keys).Select(k => k.Value));
    }

    [Fact]
    public void Dxt_textures_shrink_by_dropping_top_mips()
    {
        var dds = DdsTexture.Parse(DdsBuilder.Dxt5(16, 16));

        var small = DdsTexture.Parse(dds.ToXPlane(4));

        Assert.Equal(DdsFormat.Dxt5, small.Format);
        Assert.Equal((4, 4), (small.Width, small.Height));
        Assert.Equal(3, small.Mips.Count);  // 4, 2, 1
        Assert.All(small.Mips[0], b => Assert.Equal(2, b)); // was level 2 of the 16x16 texture
    }

    [Fact]
    public void Opaque_textures_lose_their_alpha()
    {
        var dds = DdsTexture.Parse(DdsBuilder.Dxt5(8, 8));  // blocks filled with the level number: alpha near 0

        var opaque = DdsTexture.Parse(dds.ToXPlane(8, opaque: true));

        for (var m = 0; m < opaque.Mips.Count; m++)
        {
            var level = opaque.Mips[m];
            for (var b = 0; b < level.Length; b += 16)
            {
                Assert.Equal([0xFF, 0xFF, 0, 0, 0, 0, 0, 0], level[b..(b + 8)]);
                Assert.Equal(dds.Mips[m][(b + 8)..(b + 16)], level[(b + 8)..(b + 16)]);  // colour untouched
            }
        }
    }

    [Fact]
    public void Dxt_textures_without_mips_get_a_full_chain()
    {
        var full = DdsTexture.Parse(DdsBuilder.Dxt5(16, 16));
        var single = new DdsTexture { Format = DdsFormat.Dxt5, Width = 16, Height = 16, Mips = [full.Mips[0]] };

        var converted = DdsTexture.Parse(single.ToXPlane(8));

        Assert.Equal((8, 8), (converted.Width, converted.Height));
        Assert.Equal(4, converted.Mips.Count); // 8, 4, 2, 1
    }

    [Fact]
    public void Bc7_textures_are_reencoded_as_dxt5()
    {
        var pixels = new byte[8 * 8 * 4];
        for (var i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (200, 20, 20, 255);
        var encoder = new BcEncoder(CompressionFormat.Bc7);
        encoder.OutputOptions.GenerateMipMaps = true;
        var bc7 = encoder.EncodeToRawBytes(pixels, 8, 8, PixelFormat.Rgba32);
        var source = new DdsTexture { Format = DdsFormat.Bc7, Width = 8, Height = 8, Mips = [.. bc7] };

        var converted = DdsTexture.Parse(source.ToXPlane(4));

        Assert.Equal(DdsFormat.Dxt5, converted.Format);
        Assert.Equal((4, 4), (converted.Width, converted.Height));
        Assert.Equal(3, converted.Mips.Count);
    }

    [Fact]
    public void Import_writes_a_csl_package_with_one_entry_per_livery()
    {
        using var package = new FakePackage();
        var output = Path.Combine(package.Root, "out");

        var result = FsltlImporter.Import(new ImportOptions { PackageRoot = package.Root, Output = output, MaxTextureSize = 4 });

        Assert.Equal(2, result.Liveries);
        Assert.Equal(1, result.Models);
        Assert.Equal(2, result.Textures);
        var xsb = File.ReadAllText(Path.Combine(output, "xsb_aircraft.txt"));
        Assert.StartsWith("EXPORT_NAME ClearanceLink_FSLTL", xsb);
        Assert.Contains("OBJ8_AIRCRAFT FSLTL_A20N_BAW_Speedbird\nOBJ8 SOLID YES ClearanceLink_FSLTL/objects/FSLTL_A20N_A320_0.obj FSLTL_A20N_BAW_Speedbird_TEXTURE_BAW_LIVERY_T.dds\nLIVERY A20N BAW FSLTL_A20N_BAW_Speedbird\n", xsb);
        Assert.Contains("LIVERY A20N - FSLTL_A20N\n", xsb);
        var obj = File.ReadAllText(Path.Combine(output, "objects", "FSLTL_A20N_A320_0.obj"));
        Assert.Contains("TEXTURE FSLTL_A20N_TEXTURE_LIVERY_T.dds\n", obj);
        Assert.Equal(4, DdsTexture.Load(Path.Combine(output, "objects", "FSLTL_A20N_TEXTURE_LIVERY_T.dds")).Width);
        var catalogue = FsltlImporter.LoadCatalogue(output);
        Assert.Contains(new CatalogueEntry("FSLTL_A20N_BAW-British Airways", "FSLTL_A20N_BAW_Speedbird", "A20N", "BAW", "FSLTL_A20N_A320"), catalogue);
    }

    [Fact]
    public void A_damaged_catalogue_reads_as_empty()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"cl-catalogue-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, FsltlImporter.CatalogueFile), "[{\"title\":\"FSLTL_A20N");
            Assert.Empty(FsltlImporter.LoadCatalogue(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Import_leaves_out_microsoft_models()
    {
        Assert.True(FsltlImporter.IsMicrosoftFolder("FSLTL_Asobo_C172SP"));
        Assert.False(FsltlImporter.IsMicrosoftFolder("FSLTL_A20N_BAW"));
    }

    private static List<string> Lines(string text, string prefix) =>
        [.. text.Split('\n').Where(l => l.StartsWith(prefix, StringComparison.Ordinal))];
}

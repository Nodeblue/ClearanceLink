using System.Text.Json;

namespace XPConnect.Fsltl.Tests;

/// <summary>A tiny FSLTL-shaped package on disk: one base aircraft and one livery, with a synthetic glTF model.</summary>
public sealed class FakePackage : IDisposable
{
    public FakePackage()
    {
        Root = Path.Combine(Path.GetTempPath(), "xpc-fsltl-" + Guid.NewGuid().ToString("N"));
        var planes = Path.Combine(Root, "SimObjects", "Airplanes");

        var baseDir = Directory.CreateDirectory(Path.Combine(planes, "FSLTL_A20N")).FullName;
        File.WriteAllText(Path.Combine(baseDir, "aircraft.cfg"), """
            [GENERAL]
            icao_type_designator = "A20N"

            [FLTSIM.0]
            title = "FSLTL_A20N_ZZZZ" ; base
            model = "CFM"
            texture = ""
            icao_airline = ""
            """);
        var model = Directory.CreateDirectory(Path.Combine(baseDir, "model.CFM")).FullName;
        File.WriteAllText(Path.Combine(model, "model.CFG"), "[models]\nnormal=A320.xml\n");
        File.WriteAllText(Path.Combine(model, "A320.xml"), """
            <?xml version="1.0" encoding="utf-8"?>
            <ModelInfo>
              <LODS><LOD minSize="8" ModelFile="A320_LOD0.gltf"/></LODS>
              <Behaviors>
                <Component ID="GEARS">
                  <UseTemplate Name="ASOBO_GEAR_Center_Template"><ANIM_NAME>custom_anim_GEAR_00</ANIM_NAME></UseTemplate>
                </Component>
              </Behaviors>
            </ModelInfo>
            """);
        GltfBuilder.Triangle(Path.Combine(model, "A320_LOD0.gltf"), animateGear: true);
        var texture = Directory.CreateDirectory(Path.Combine(baseDir, "TEXTURE")).FullName;
        File.WriteAllBytes(Path.Combine(texture, "LIVERY_T.PNG.DDS"), DdsBuilder.Dxt5(8, 8));

        var liveryDir = Directory.CreateDirectory(Path.Combine(planes, "FSLTL_A20N_BAW Speedbird")).FullName;
        File.WriteAllText(Path.Combine(liveryDir, "aircraft.cfg"), """
            [VARIATION]
            base_container = "..\FSLTL_A20N"

            [FLTSIM.0]
            title = "FSLTL_A20N_BAW-British Airways"
            model = "BAW"
            texture = "BAW"
            icao_airline = "baw"
            """);
        var liveryModel = Directory.CreateDirectory(Path.Combine(liveryDir, "MODEL.BAW")).FullName;
        File.WriteAllText(Path.Combine(liveryModel, "model.CFG"), "[models]\nnormal=..\\..\\FSLTL_A20N\\model.CFM\\A320.xml\n");
        var liveryTexture = Directory.CreateDirectory(Path.Combine(liveryDir, "TEXTURE.BAW")).FullName;
        File.WriteAllText(Path.Combine(liveryTexture, "texture.CFG"), "[fltsim]\nfallback.1=..\\..\\FSLTL_A20N\\TEXTURE\n");
        File.WriteAllBytes(Path.Combine(liveryTexture, "LIVERY_T.PNG.DDS"), DdsBuilder.Dxt5(8, 8));
    }

    public string Root { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Writes small glTF files the way Asobo's exporter packs them (half-float UVs, byte normals).</summary>
public static class GltfBuilder
{
    /// <summary>
    /// One triangle under a node "gear" at (1, 2, 3), material "livery" textured LIVERY_T.PNG.DDS. With
    /// <paramref name="animateGear"/>, custom_anim_GEAR_00 turns the node 90° about X over frames 0..100.
    /// </summary>
    public static void Triangle(string path, bool animateGear)
    {
        var bin = new MemoryStream();
        var w = new BinaryWriter(bin);
        // One tightly packed buffer view per accessor.
        float[][] positions = [[0, 0, 0], [1, 0, 0], [0, 1, 0]];
        foreach (var p in positions) foreach (var c in p) w.Write(c);                       // view 0: 36 bytes
        foreach (var _ in positions) { w.Write((sbyte)0); w.Write((sbyte)0); w.Write((sbyte)127); w.Write((sbyte)0); } // view 1: 12
        foreach (var (u, v) in new[] { (0f, 0f), (1f, 0f), (0f, 1f) }) { w.Write((Half)u); w.Write((Half)v); }          // view 2: 12
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)2); w.Write((ushort)0);                                  // view 3: 6 (+2 pad)
        w.Write(0f); w.Write(100f);                                                                                     // view 4: times
        var half = MathF.Sqrt(0.5f);
        foreach (var q in new[] { new[] { 0f, 0f, 0f, 1f }, new[] { half, 0f, 0f, half } }) foreach (var c in q) w.Write(c); // view 5: rotations
        w.Flush();
        File.WriteAllBytes(Path.ChangeExtension(path, ".bin"), bin.ToArray());

        var gltf = new Dictionary<string, object>
        {
            ["asset"] = new { version = "2.0", extensions = new { ASOBO_asset_optimized = new { } } },
            ["extensionsRequired"] = new[] { "MSFT_texture_dds" },
            ["scene"] = 0,
            ["scenes"] = new[] { new { nodes = new[] { 0 } } },
            ["nodes"] = new object[]
            {
                new { name = "root", children = new[] { 1 } },
                new { name = "gear", mesh = 0, translation = new[] { 1f, 2f, 3f } },
            },
            ["meshes"] = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0, NORMAL = 1, TEXCOORD_0 = 2 }, indices = 3, material = 0 } } } },
            ["materials"] = new[] { new { name = "livery", alphaMode = "MASK", pbrMetallicRoughness = new { baseColorTexture = new { index = 0 } } } },
            ["textures"] = new[] { new { extensions = new { MSFT_texture_dds = new { source = 0 } } } },
            ["images"] = new[] { new { uri = "LIVERY_T.PNG.DDS" } },
            ["buffers"] = new[] { new { uri = Path.GetFileName(Path.ChangeExtension(path, ".bin")), byteLength = bin.Length } },
            ["bufferViews"] = new[]
            {
                new { buffer = 0, byteOffset = 0, byteLength = 36 },
                new { buffer = 0, byteOffset = 36, byteLength = 12 },
                new { buffer = 0, byteOffset = 48, byteLength = 12 },
                new { buffer = 0, byteOffset = 60, byteLength = 6 },
                new { buffer = 0, byteOffset = 68, byteLength = 8 },
                new { buffer = 0, byteOffset = 76, byteLength = 32 },
            },
            ["accessors"] = new object[]
            {
                new { bufferView = 0, componentType = 5126, count = 3, type = "VEC3" },
                new { bufferView = 1, componentType = 5120, count = 3, type = "VEC4" },
                new { bufferView = 2, componentType = 5122, count = 3, type = "VEC2" },
                new { bufferView = 3, componentType = 5123, count = 3, type = "SCALAR" },
                new { bufferView = 4, componentType = 5126, count = 2, type = "SCALAR" },
                new { bufferView = 5, componentType = 5126, count = 2, type = "VEC4" },
            },
        };
        if (animateGear)
        {
            gltf["animations"] = new[]
            {
                new
                {
                    name = "custom_anim_GEAR_00",
                    samplers = new[] { new { input = 4, output = 5 } },
                    channels = new[] { new { sampler = 0, target = new { node = 1, path = "rotation" } } },
                },
            };
        }

        File.WriteAllText(path, JsonSerializer.Serialize(gltf));
    }
}

public static class DdsBuilder
{
    /// <summary>A DXT5 texture with a full mip chain; each level's blocks are filled with the level number.</summary>
    public static byte[] Dxt5(int width, int height)
    {
        var mips = new List<byte[]>();
        for (int w = width, h = height, level = 0; ; w = Math.Max(1, w / 2), h = Math.Max(1, h / 2), level++)
        {
            var bytes = new byte[DdsTexture.LevelSize(DdsFormat.Dxt5, w, h)];
            Array.Fill(bytes, (byte)level);
            mips.Add(bytes);
            if (w == 1 && h == 1) break;
        }

        return DdsTexture.Write(DdsFormat.Dxt5, width, height, mips);
    }
}

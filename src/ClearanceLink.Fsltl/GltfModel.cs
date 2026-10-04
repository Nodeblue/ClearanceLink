using System.Numerics;
using System.Text.Json;

namespace ClearanceLink.Fsltl;

/// <summary>
/// A glTF 2.0 model as exported for MSFS, decoded to plain floats. Handles Asobo's "optimized" vertex packing
/// (<c>ASOBO_asset_optimized</c>): texture coordinates are half floats stored in SHORT accessors and normals are
/// signed bytes, both without the glTF <c>normalized</c> flag.
/// </summary>
public sealed class GltfModel
{
    public required string Path { get; init; }
    public required List<GltfNode> Nodes { get; init; }
    public required List<int> Roots { get; init; }
    public required List<List<GltfPrimitive>> Meshes { get; init; }
    public required List<GltfMaterial> Materials { get; init; }
    public required List<GltfAnimation> Animations { get; init; }

    public static GltfModel Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        var dir = System.IO.Path.GetDirectoryName(path)!;
        var buffers = Array(root, "buffers")
            .Select(b => b.TryGetProperty("uri", out var uri) && FileLookup.Child(dir, uri.GetString()!) is { } file
                ? File.ReadAllBytes(file)
                : throw new InvalidDataException($"{path}: buffer file missing"))
            .ToList();
        var optimized = root.TryGetProperty("asset", out var asset) && asset.TryGetProperty("extensions", out var ext)
            && ext.TryGetProperty("ASOBO_asset_optimized", out _);
        var reader = new AccessorReader(root, buffers, optimized);

        var images = Array(root, "images").Select(i => i.TryGetProperty("uri", out var u) ? u.GetString() ?? "" : "").ToList();
        var textures = Array(root, "textures").Select(t => TextureImage(t, images)).ToList();
        string? Tex(JsonElement owner, string name) =>
            owner.TryGetProperty(name, out var info) && info.TryGetProperty("index", out var index)
                && index.GetInt32() is var i && i >= 0 && i < textures.Count
                ? textures[i]
                : null;

        var materials = Array(root, "materials").Select(m => new GltfMaterial(
            m.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            m.TryGetProperty("pbrMetallicRoughness", out var pbr) ? Tex(pbr, "baseColorTexture") : null,
            Tex(m, "emissiveTexture"),
            m.TryGetProperty("alphaMode", out var mode) ? mode.GetString() ?? "OPAQUE" : "OPAQUE",
            m.TryGetProperty("alphaCutoff", out var cutoff) ? cutoff.GetSingle() : 0.5f,
            m.TryGetProperty("doubleSided", out var sided) && sided.GetBoolean())).ToList();

        var meshes = Array(root, "meshes").Select(m => Array(m, "primitives")
            .Where(p => !p.TryGetProperty("mode", out var mode) || mode.GetInt32() == 4)
            .Select(p => reader.Primitive(p))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList()).ToList();

        var nodes = Array(root, "nodes").Select(n => new GltfNode(
            n.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            n.TryGetProperty("mesh", out var mesh) ? mesh.GetInt32() : -1,
            n.TryGetProperty("children", out var children) ? children.EnumerateArray().Select(c => c.GetInt32()).ToList() : [],
            n.TryGetProperty("translation", out var t) ? Vec3(t) : Vector3.Zero,
            n.TryGetProperty("rotation", out var r) ? Quat(r) : Quaternion.Identity,
            n.TryGetProperty("scale", out var s) ? Vec3(s) : Vector3.One,
            n.TryGetProperty("matrix", out var matrix) ? Matrix(matrix) : null)).ToList();

        var roots = root.TryGetProperty("scenes", out var scenes) && scenes.GetArrayLength() > 0
            ? Array(scenes[root.TryGetProperty("scene", out var sc) ? sc.GetInt32() : 0], "nodes").Select(n => n.GetInt32()).ToList()
            : Enumerable.Range(0, nodes.Count).Where(i => !nodes.Any(n => n.Children.Contains(i))).ToList();

        var animations = Array(root, "animations").Select(a =>
        {
            var samplers = Array(a, "samplers").ToList();
            var channels = new List<GltfChannel>();
            foreach (var c in Array(a, "channels"))
            {
                var target = c.GetProperty("target");
                if (!target.TryGetProperty("node", out var node)) continue;
                var sampler = samplers[c.GetProperty("sampler").GetInt32()];
                var times = reader.Floats(sampler.GetProperty("input").GetInt32(), 1);
                var path = target.GetProperty("path").GetString() ?? "";
                var width = path == "rotation" ? 4 : 3;
                var values = reader.Floats(sampler.GetProperty("output").GetInt32(), width);
                channels.Add(new GltfChannel(node.GetInt32(), path, times, values, width));
            }

            return new GltfAnimation(a.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "", channels);
        }).ToList();

        return new GltfModel { Path = path, Nodes = nodes, Roots = roots, Meshes = meshes, Materials = materials, Animations = animations };
    }

    private static List<JsonElement> Array(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array ? [.. array.EnumerateArray()] : [];

    private static string? TextureImage(JsonElement texture, List<string> images)
    {
        // MSFS textures point at their .DDS image through MSFT_texture_dds; plain "source" is the fallback.
        if (texture.TryGetProperty("extensions", out var ext) && ext.TryGetProperty("MSFT_texture_dds", out var dds)
            && dds.TryGetProperty("source", out var ddsSource))
        {
            return images.ElementAtOrDefault(ddsSource.GetInt32());
        }

        return texture.TryGetProperty("source", out var source) ? images.ElementAtOrDefault(source.GetInt32()) : null;
    }

    private static Vector3 Vec3(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());

    private static Quaternion Quat(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle(), e[3].GetSingle());

    private static Matrix4x4 Matrix(JsonElement e)
    {
        // glTF matrices are column-major with column vectors; System.Numerics uses row vectors, so the same 16
        // numbers read row by row give the transposed (row-vector) matrix directly.
        var m = new float[16];
        for (var i = 0; i < 16; i++) m[i] = e[i].GetSingle();
        return new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
    }

    private sealed class AccessorReader(JsonElement root, List<byte[]> buffers, bool optimized)
    {
        private readonly List<JsonElement> _accessors = Array(root, "accessors").ToList();
        private readonly List<JsonElement> _views = Array(root, "bufferViews").ToList();

        public GltfPrimitive? Primitive(JsonElement p)
        {
            var attributes = p.GetProperty("attributes");
            if (!attributes.TryGetProperty("POSITION", out var position) || !p.TryGetProperty("indices", out var indicesAccessor)) return null;
            var positions = Floats(position.GetInt32(), 3);
            var count = positions.Length / 3;
            var normals = attributes.TryGetProperty("NORMAL", out var normal) ? Floats(normal.GetInt32(), 3) : new float[count * 3];
            var uvs = attributes.TryGetProperty("TEXCOORD_0", out var uv) ? Floats(uv.GetInt32(), 2) : new float[count * 2];
            var indices = Ints(indicesAccessor.GetInt32());
            if (indices.Any(i => i < 0 || i >= count)) return null;
            return new GltfPrimitive(positions, normals, uvs, indices, p.TryGetProperty("material", out var material) ? material.GetInt32() : -1);
        }

        /// <summary>An accessor as floats, <paramref name="width"/> per element (extra components dropped).</summary>
        public float[] Floats(int index, int width)
        {
            var a = _accessors[index];
            var type = a.GetProperty("componentType").GetInt32();
            var components = Components(a.GetProperty("type").GetString());
            var normalized = a.TryGetProperty("normalized", out var n) && n.GetBoolean();
            var (bytes, offset, stride, count) = Locate(a, components * Size(type));
            var result = new float[count * width];
            var take = Math.Min(width, components);
            for (var e = 0; e < count; e++)
            {
                var at = offset + e * stride;
                for (var c = 0; c < take; c++) result[e * width + c] = Read(bytes, at + c * Size(type), type, normalized);
            }

            return result;
        }

        private float Read(byte[] b, int at, int type, bool normalized) => type switch
        {
            5126 => BitConverter.ToSingle(b, at),
            // Asobo's optimized files keep half floats in SHORT accessors (texture coordinates).
            5122 when optimized && !normalized => (float)BitConverter.ToHalf(b, at),
            5122 => normalized ? Math.Max(BitConverter.ToInt16(b, at) / 32767f, -1f) : BitConverter.ToInt16(b, at),
            5123 => normalized ? BitConverter.ToUInt16(b, at) / 65535f : BitConverter.ToUInt16(b, at),
            // Signed bytes are normals and tangents in MSFS files, normalized whether flagged or not.
            5120 => Math.Max((sbyte)b[at] / 127f, -1f),
            5121 => normalized ? b[at] / 255f : b[at],
            5125 => BitConverter.ToUInt32(b, at),
            _ => 0f,
        };

        private int[] Ints(int index)
        {
            var a = _accessors[index];
            var type = a.GetProperty("componentType").GetInt32();
            var (bytes, offset, stride, count) = Locate(a, Size(type));
            var result = new int[count];
            for (var e = 0; e < count; e++)
            {
                var at = offset + e * stride;
                result[e] = type switch
                {
                    5121 => bytes[at],
                    5123 => BitConverter.ToUInt16(bytes, at),
                    _ => (int)BitConverter.ToUInt32(bytes, at),
                };
            }

            return result;
        }

        private (byte[] Bytes, int Offset, int Stride, int Count) Locate(JsonElement a, int elementSize)
        {
            var view = _views[a.GetProperty("bufferView").GetInt32()];
            var bytes = buffers[view.GetProperty("buffer").GetInt32()];
            var offset = (view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0)
                + (a.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);
            var stride = view.TryGetProperty("byteStride", out var s) ? s.GetInt32() : elementSize;
            var count = a.GetProperty("count").GetInt32();
            if (count > 0 && offset + (count - 1) * stride + elementSize > bytes.Length)
            {
                throw new InvalidDataException("accessor runs past the end of its buffer");
            }

            return (bytes, offset, stride, count);
        }

        private static int Components(string? type) => type switch
        {
            "SCALAR" => 1,
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" => 4,
            "MAT4" => 16,
            _ => 1,
        };

        private static int Size(int componentType) => componentType switch
        {
            5120 or 5121 => 1,
            5122 or 5123 => 2,
            _ => 4,
        };
    }
}

public sealed record GltfNode(string Name, int Mesh, List<int> Children, Vector3 Translation, Quaternion Rotation, Vector3 Scale, Matrix4x4? Matrix);

/// <summary>Triangles: 3 floats per position and normal, 2 per UV (glTF orientation: v = 0 at the image top).</summary>
public sealed record GltfPrimitive(float[] Positions, float[] Normals, float[] Uvs, int[] Indices, int Material);

public sealed record GltfMaterial(string Name, string? BaseColor, string? Emissive, string AlphaMode, float AlphaCutoff, bool DoubleSided);

public sealed record GltfAnimation(string Name, List<GltfChannel> Channels);

/// <summary>One animated property: <paramref name="Width"/> values per key (4 for rotation quaternions x,y,z,w).</summary>
public sealed record GltfChannel(int Node, string Path, float[] Times, float[] Values, int Width)
{
    public float MaxTime => Times.Length == 0 ? 0 : Times[^1];

    /// <summary>Whether the keys move the part at all (some channels only hold one pose for the whole animation).</summary>
    public bool Varies
    {
        get
        {
            for (var k = Width; k < Values.Length; k++)
            {
                if (MathF.Abs(Values[k] - Values[k % Width]) > 1e-4f) return true;
            }

            return false;
        }
    }

    public Vector3 Vec3At(int key) => new(Values[key * 3], Values[key * 3 + 1], Values[key * 3 + 2]);

    public Quaternion QuatAt(int key) => Quaternion.Normalize(new Quaternion(Values[key * 4], Values[key * 4 + 1], Values[key * 4 + 2], Values[key * 4 + 3]));

    /// <summary>Linearly interpolated (slerp for rotations) value at <paramref name="time"/>, clamped to the keys.</summary>
    public Vector3 Vec3(float time)
    {
        var (i, j, f) = Span(time);
        return Vector3.Lerp(Vec3At(i), Vec3At(j), f);
    }

    public Quaternion Quat(float time)
    {
        var (i, j, f) = Span(time);
        return Quaternion.Slerp(QuatAt(i), QuatAt(j), f);
    }

    private (int, int, float) Span(float time)
    {
        if (Times.Length == 0) return (0, 0, 0);
        if (time <= Times[0]) return (0, 0, 0);
        for (var k = 1; k < Times.Length; k++)
        {
            if (time <= Times[k])
            {
                var span = Times[k] - Times[k - 1];
                return (k - 1, k, span <= 0 ? 1 : (time - Times[k - 1]) / span);
            }
        }

        return (Times.Length - 1, Times.Length - 1, 0);
    }
}

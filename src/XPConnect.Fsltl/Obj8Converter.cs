using System.Globalization;
using System.Numerics;
using System.Text;

namespace XPConnect.Fsltl;

/// <summary>One X-Plane .obj made from the glTF geometry that uses one texture pair.</summary>
/// <param name="BaseColor">glTF image name of the albedo texture (resolved per livery by the importer).</param>
/// <param name="Emissive">glTF image name of the emissive texture, used as X-Plane's _LIT texture.</param>
/// <param name="Text">The .obj file with <c>{TEXTURE}</c> and <c>{TEXTURE_LIT}</c> placeholders for the texture lines.</param>
public sealed record Obj8Part(string BaseColor, string? Emissive, string Text, int Triangles, int Vertices);

public sealed record Obj8Result(List<Obj8Part> Parts, List<string> Warnings, float Size);

/// <summary>
/// Converts MSFS glTF models (one per LOD) into X-Plane OBJ8 text, one object per texture: an .obj has a single
/// texture, so the livery body and e.g. the window material become separate objects drawn together.
///
/// Coordinates: MSFS models are Y up with the nose towards +Z (X to the left); X-Plane objects are Y up with the
/// nose towards -Z (X to the right): a half turn about Y. Animated parts keep the glTF node hierarchy as nested
/// ANIM blocks; still parts are baked into their vertices. Triangles are written clockwise, as X-Plane expects
/// (whichever way the glTF wound them).
/// </summary>
public static class Obj8Converter
{
    private static readonly Matrix4x4 ToXPlane = Matrix4x4.CreateRotationY(MathF.PI);

    /// <param name="lods">The glTF files, most detailed first; at most two are used (the first and the last).</param>
    public static Obj8Result Convert(IReadOnlyList<GltfModel> lods, IReadOnlyDictionary<string, AnimationRole> roles)
    {
        var warnings = new List<string>();
        var used = lods.Count > 1 && lods[^1].Path != lods[0].Path ? new[] { lods[0], lods[^1] } : new[] { lods[0] };
        var size = Size(used[0]);
        // FSLTL's low LOD is ~1.5k triangles: only once the aircraft is a few pixels (about 2 km for an A320).
        var switchDistance = MathF.Max(1500, 60 * size);
        var ranges = used.Length == 1 ? new[] { (0f, 30000f) } : new[] { (0f, switchDistance), (switchDistance, 30000f) };

        var groups = used.SelectMany(m => m.Materials).Where(m => m.BaseColor is not null)
            .Select(m => (m.BaseColor!, m.Emissive)).Distinct().ToList();
        var parts = new List<Obj8Part>();
        foreach (var (baseColor, emissive) in groups)
        {
            var builder = new ObjBuilder();
            for (var l = 0; l < used.Length; l++)
            {
                var scene = new SceneWalker(used[l], roles, baseColor, emissive, builder, warnings);
                if (!used[l].Roots.Any(scene.HasGeometry)) continue;  // e.g. windows that the low LOD paints on
                builder.Command($"ATTR_LOD {Num(ranges[l].Item1)} {Num(ranges[l].Item2)}");
                builder.ResetState();
                foreach (var root in used[l].Roots) scene.Emit(root, ToXPlane);
            }

            if (builder.Triangles > 0) parts.Add(new Obj8Part(baseColor, emissive, builder.Text(emissive is not null), builder.Triangles, builder.VertexCount));
        }

        var untextured = used[0].Materials.Count(m => m.BaseColor is null);
        if (untextured > 0) warnings.Add($"{untextured} material(s) without a texture left out");
        return new Obj8Result(parts, warnings.Distinct().ToList(), size);
    }

    /// <summary>The largest dimension of the model's still geometry, in metres.</summary>
    public static float Size(GltfModel model)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        void Walk(int index, Matrix4x4 parent)
        {
            var node = model.Nodes[index];
            var world = Local(node) * parent;
            if (node.Mesh >= 0)
            {
                foreach (var p in model.Meshes[node.Mesh])
                {
                    for (var i = 0; i < p.Positions.Length; i += 3)
                    {
                        var v = Vector3.Transform(new Vector3(p.Positions[i], p.Positions[i + 1], p.Positions[i + 2]), world);
                        min = Vector3.Min(min, v);
                        max = Vector3.Max(max, v);
                    }
                }
            }

            foreach (var c in node.Children) Walk(c, world);
        }

        foreach (var r in model.Roots) Walk(r, Matrix4x4.Identity);
        if (min.X > max.X) return 0;
        var extent = max - min;
        return MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));
    }

    internal static Matrix4x4 Local(GltfNode node) =>
        node.Matrix ?? Matrix4x4.CreateScale(node.Scale) * Matrix4x4.CreateFromQuaternion(node.Rotation) * Matrix4x4.CreateTranslation(node.Translation);

    /// <summary>Drops animation keys that lie on the straight line between their neighbours.</summary>
    internal static List<(float Value, float[] Data)> SimplifyKeys(List<(float Value, float[] Data)> keys) => SceneWalker.Simplify(keys);

    internal static bool IsCounterClockwise(GltfPrimitive p) => ObjBuilder.IsCounterClockwise(p);

    internal static string Num(float v)
    {
        var r = MathF.Round(v, 5);
        return (r == 0 ? 0 : r).ToString("0.#####", CultureInfo.InvariantCulture);
    }

    /// <summary>A node's animated channels after the model XML decided what drives them.</summary>
    private sealed record Driven(GltfChannel Channel, AnimationRole Role, float Length);

    private sealed class SceneWalker
    {
        private readonly GltfModel _model;
        private readonly string _baseColor;
        private readonly string? _emissive;
        private readonly ObjBuilder _out;
        private readonly List<string> _warnings;
        private readonly Dictionary<int, List<Driven>> _channels = [];
        private readonly Dictionary<int, bool> _hasGeometry = [];

        public SceneWalker(GltfModel model, IReadOnlyDictionary<string, AnimationRole> roles, string baseColor, string? emissive, ObjBuilder output, List<string> warnings)
        {
            _model = model;
            _baseColor = baseColor;
            _emissive = emissive;
            _out = output;
            _warnings = warnings;
            foreach (var animation in model.Animations)
            {
                var (role, key) = RoleFor(animation.Name, roles);
                // Blender splits one MSFS animation per node ("<name>_node30_anim_..."); its length is the longest part.
                var length = model.Animations.Where(a => key is null ? a == animation : RoleFor(a.Name, roles).Key == key)
                    .SelectMany(a => a.Channels).Select(c => c.MaxTime).DefaultIfEmpty(0).Max();
                foreach (var channel in animation.Channels)
                {
                    if (!_channels.TryGetValue(channel.Node, out var list)) _channels[channel.Node] = list = [];
                    list.Add(new Driven(channel, role, length <= 0 ? 1 : length));
                }
            }
        }

        private static (AnimationRole Role, string? Key) RoleFor(string name, IReadOnlyDictionary<string, AnimationRole> roles)
        {
            if (roles.TryGetValue(name, out var exact)) return (exact, name);
            foreach (var (key, role) in roles)
            {
                if (name.StartsWith(key + "_node", StringComparison.OrdinalIgnoreCase)) return (role, key);
            }

            // Not in the model XML (an included behaviour file drives it): judge by name; parts split per node share a key.
            var byName = AnimationRoles.FromName(name);
            var node = name.IndexOf("_node", StringComparison.OrdinalIgnoreCase);
            return byName.Dataref is null ? (AnimationRole.Still, null) : (byName, node > 0 ? name[..node] : name);
        }

        /// <summary>Writes node <paramref name="index"/> and its children; <paramref name="frame"/> maps the node's parent space to the current ANIM space.</summary>
        public void Emit(int index, Matrix4x4 frame)
        {
            if (!HasGeometry(index)) return;
            var node = _model.Nodes[index];
            var channels = _channels.GetValueOrDefault(index) ?? [];
            Driven? Pick(string path) =>
                channels.Where(c => c.Channel.Path == path).OrderBy(c => c.Role.Dataref is null ? 1 : 0).FirstOrDefault();
            var translation = Pick("translation");
            var rotation = Pick("rotation");
            var scale = Pick("scale");

            // Still poses: animations nothing in X-Plane drives are frozen at their pose, as if the node never moved.
            var t = translation is null ? node.Translation : translation.Channel.Vec3(Pose(translation));
            var r = rotation is null ? node.Rotation : rotation.Channel.Quat(Pose(rotation));
            var s = scale is null ? node.Scale : scale.Channel.Vec3(Pose(scale));
            var hide = scale is { Role.Dataref: not null } ? HiddenRanges(scale) : [];
            var moves = translation is { Role.Dataref: not null } || rotation is { Role.Dataref: not null } || hide.Count > 0;

            if (!moves)
            {
                if (MathF.Abs(s.X * s.Y * s.Z) < 1e-9f) return;  // Scaled to nothing at its pose: hidden.
                var local = node.Matrix ?? Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);
                var world = local * frame;
                Geometry(node, world);
                foreach (var c in node.Children) Emit(c, world);
                return;
            }

            _out.Command("ANIM_begin");
            // OBJ8 cannot scale, so a scaled parent (FSLTL's SF34 scales a parent of its gear) is pushed
            // inside: it scales this node's moves and its geometry instead.
            var parentScale = StaticTransform(frame);
            t *= parentScale;
            if (translation is { Role.Dataref: { } tref })
            {
                _out.Command($"ANIM_trans_begin {tref}");
                var moves3 = Keys(translation).Select(k => (k.Value, Data: new[] { translation.Channel.Vec3At(k.Key).X * parentScale.X, translation.Channel.Vec3At(k.Key).Y * parentScale.Y, translation.Channel.Vec3At(k.Key).Z * parentScale.Z })).ToList();
                foreach (var (value, data) in Simplify(moves3)) _out.Command($"ANIM_trans_key {Num(value)} {Num(data[0])} {Num(data[1])} {Num(data[2])}");
                _out.Command("ANIM_trans_end");
            }
            else if (t != Vector3.Zero)
            {
                _out.Command($"ANIM_trans {Vec(t)} {Vec(t)} 0 0 none");
            }

            if (rotation is { Role.Dataref: { } rref })
            {
                KeyedRotation(rotation, rref, node.Name);
            }
            else
            {
                StaticRotation(r);
            }

            foreach (var (from, to) in hide) _out.Command($"ANIM_hide {Num(from)} {Num(to)} {scale!.Role.Dataref}");
            var inner = Matrix4x4.CreateScale((hide.Count > 0 ? Vector3.One : s) * parentScale);
            Geometry(node, inner);
            foreach (var c in node.Children) Emit(c, inner);
            _out.Command("ANIM_end");
        }

        private static float Pose(Driven d) => d.Role.PoseRatio * d.Length;

        /// <summary>Drops keys that lie on the straight line between their neighbours (MSFS exports a key per frame).</summary>
        internal static List<(float Value, float[] Data)> Simplify(List<(float Value, float[] Data)> keys)
        {
            if (keys.Count <= 2) return keys;
            var kept = new List<(float Value, float[] Data)> { keys[0] };
            for (var i = 1; i < keys.Count - 1; i++)
            {
                var (v0, d0) = kept[^1];
                var (v1, d1) = keys[i];
                var (v2, d2) = keys[i + 1];
                var f = (v1 - v0) / (v2 - v0);
                var straight = true;
                for (var c = 0; c < d1.Length && straight; c++) straight = MathF.Abs(d0[c] + (d2[c] - d0[c]) * f - d1[c]) < 1e-3f;
                if (!straight) kept.Add(keys[i]);
            }

            kept.Add(keys[^1]);
            return kept;
        }

        private static IEnumerable<(float Value, int Key)> Keys(Driven d)
        {
            float? last = null;
            for (var k = 0; k < d.Channel.Times.Length; k++)
            {
                var value = d.Role.Value(Math.Clamp(d.Channel.Times[k] / d.Length, 0, 1));
                if (last is { } previous && value <= previous) continue;  // X-Plane needs rising key values.
                last = value;
                yield return (value, k);
            }
        }

        /// <summary>
        /// OBJ8 rotations turn about one fixed axis. glTF keys are full quaternions, so each key is written as a turn
        /// about the axis of the largest change from the first key, followed by the first key's rotation.
        /// </summary>
        private void KeyedRotation(Driven d, string dataref, string nodeName)
        {
            var channel = d.Channel;
            var first = channel.QuatAt(0);
            var inverseFirst = Quaternion.Conjugate(first);
            var deltas = Enumerable.Range(0, channel.Times.Length).Select(k => Quaternion.Normalize(channel.QuatAt(k) * inverseFirst)).ToList();
            var axis = Vector3.Zero;
            var largest = 0f;
            foreach (var q in deltas)
            {
                var v = new Vector3(q.X, q.Y, q.Z);
                if (v.Length() > largest)
                {
                    largest = v.Length();
                    axis = v * MathF.Sign(q.W == 0 ? 1 : q.W);
                }
            }

            if (largest < 1e-6f)
            {
                StaticRotation(first);
                return;
            }

            axis = Vector3.Normalize(axis);
            var offAxis = false;
            var angles = new List<(float Value, float[] Data)>();
            float? previous = null;
            foreach (var (value, key) in Keys(d))
            {
                var q = deltas[key];
                var v = new Vector3(q.X, q.Y, q.Z);
                if (Vector3.Cross(v, axis).Length() > 0.02f) offAxis = true;
                var angle = 2 * MathF.Atan2(Vector3.Dot(v, axis), q.W) * 180 / MathF.PI;
                // Exporters store the shortest arc, so a wheel jumps from +171° to -171°; keep turning the same way.
                if (previous is { } p)
                {
                    while (angle - p > 180) angle -= 360;
                    while (angle - p < -180) angle += 360;
                }

                previous = angle;
                angles.Add((value, [angle]));
            }

            _out.Command($"ANIM_rotate_begin {Vec(axis)} {dataref}");
            foreach (var (value, data) in Simplify(angles)) _out.Command($"ANIM_rotate_key {Num(value)} {Num(data[0])}");

            _out.Command("ANIM_rotate_end");
            if (offAxis) _warnings.Add($"node {nodeName}: rotation is not about one axis; approximated");
            StaticRotation(first);
        }

        /// <summary>Value ranges in which the scale key is (near) zero, i.e. the part is hidden.</summary>
        private static List<(float From, float To)> HiddenRanges(Driven d)
        {
            var ranges = new List<(float, float)>();
            float? start = null;
            float last = 0;
            foreach (var (value, key) in Keys(d))
            {
                var v = d.Channel.Vec3At(key);
                var hidden = MathF.Abs(v.X * v.Y * v.Z) < 1e-6f;
                if (hidden && start is null) start = value;
                if (!hidden && start is { } s)
                {
                    ranges.Add((s, last));
                    start = null;
                }

                last = value;
            }

            if (start is { } open) ranges.Add((open, last));
            return ranges;
        }

        /// <summary>Writes the rotation and translation of <paramref name="frame"/>; returns its scale, which OBJ8 cannot express.</summary>
        private Vector3 StaticTransform(Matrix4x4 frame)
        {
            if (frame.IsIdentity) return Vector3.One;
            if (!Matrix4x4.Decompose(frame, out var scale, out var rotation, out var translation))
            {
                _warnings.Add("parent transform of an animated part cannot be decomposed; ignored");
                return Vector3.One;
            }

            // A scale that is not the same on every axis does not commute with the part's own rotation: approximate.
            if (MathF.Abs(scale.X - scale.Y) > 0.01f * MathF.Abs(scale.X) || MathF.Abs(scale.X - scale.Z) > 0.01f * MathF.Abs(scale.X))
            {
                _warnings.Add("unevenly scaled parent of an animated part: approximated");
            }

            if (translation != Vector3.Zero) _out.Command($"ANIM_trans {Vec(translation)} {Vec(translation)} 0 0 none");
            StaticRotation(rotation);
            return scale;
        }

        private void StaticRotation(Quaternion q)
        {
            q = Quaternion.Normalize(q);
            if (q.W < 0) q = -q;
            var sin = MathF.Sqrt(MathF.Max(0, 1 - q.W * q.W));
            if (sin < 1e-6f) return;
            var angle = 2 * MathF.Acos(Math.Clamp(q.W, -1, 1)) * 180 / MathF.PI;
            var axis = new Vector3(q.X, q.Y, q.Z) / sin;
            _out.Command($"ANIM_rotate {Vec(axis)} {Num(angle)} {Num(angle)} 0 0 none");
        }

        private void Geometry(GltfNode node, Matrix4x4 transform)
        {
            if (node.Mesh < 0) return;
            foreach (var p in _model.Meshes[node.Mesh])
            {
                if (!Uses(p)) continue;
                var material = p.Material >= 0 && p.Material < _model.Materials.Count ? _model.Materials[p.Material] : null;
                _out.State(material);
                _out.AddTriangles(p, transform);
            }
        }

        private bool Uses(GltfPrimitive p) =>
            p.Material >= 0 && p.Material < _model.Materials.Count
            && _model.Materials[p.Material] is { BaseColor: { } baseColor } m
            && baseColor == _baseColor && m.Emissive == _emissive;

        public bool HasGeometry(int index)
        {
            if (_hasGeometry.TryGetValue(index, out var known)) return known;
            var node = _model.Nodes[index];
            var has = (node.Mesh >= 0 && _model.Meshes[node.Mesh].Any(Uses)) || node.Children.Any(HasGeometry);
            _hasGeometry[index] = has;
            return has;
        }

        private static string Vec(Vector3 v) => $"{Num(v.X)} {Num(v.Y)} {Num(v.Z)}";
    }

    private sealed class ObjBuilder
    {
        private readonly StringBuilder _vertices = new();
        private readonly List<int> _indices = [];
        private readonly List<string> _commands = [];
        private int _trisStart = -1;
        private int _trisCount;
        private string? _blend;
        private bool? _cull;

        public int VertexCount { get; private set; }

        public int Triangles { get; private set; }

        public void Command(string command)
        {
            Flush();
            _commands.Add(command);
        }

        public void ResetState()
        {
            _blend = null;
            _cull = null;
        }

        public void State(GltfMaterial? material)
        {
            var blend = material?.AlphaMode switch
            {
                "BLEND" => "ATTR_blend",
                "MASK" => $"ATTR_no_blend {Num(material.AlphaCutoff)}",
                _ => "ATTR_no_blend 0",
            };
            if (blend != _blend)
            {
                Command(blend);
                _blend = blend;
            }

            var cull = material is not { DoubleSided: true };
            if (cull != _cull)
            {
                Command(cull ? "ATTR_cull" : "ATTR_no_cull");
                _cull = cull;
            }
        }

        /// <summary>
        /// True when the primitive's triangles face where their vertices run counter-clockwise: the counter-clockwise
        /// face normals agree with the stored vertex normals over the primitive as a whole.
        /// </summary>
        internal static bool IsCounterClockwise(GltfPrimitive p)
        {
            double agreement = 0;
            for (var i = 0; i + 2 < p.Indices.Length; i += 3) agreement += Agreement(p, i);
            return agreement > 0;
        }

        /// <summary>Positive when triangle <paramref name="first"/>'s counter-clockwise normal agrees with its vertex normals.</summary>
        private static double Agreement(GltfPrimitive p, int first)
        {
            Vector3 At(float[] a, int i) => new(a[i * 3], a[i * 3 + 1], a[i * 3 + 2]);
            int a = p.Indices[first], b = p.Indices[first + 1], c = p.Indices[first + 2];
            var face = Vector3.Cross(At(p.Positions, b) - At(p.Positions, a), At(p.Positions, c) - At(p.Positions, a));
            return Vector3.Dot(face, At(p.Normals, a) + At(p.Normals, b) + At(p.Normals, c));
        }

        public void AddTriangles(GltfPrimitive p, Matrix4x4 transform)
        {
            var baseVertex = VertexCount;
            for (var i = 0; i < p.Positions.Length / 3; i++)
            {
                var pos = Vector3.Transform(new Vector3(p.Positions[i * 3], p.Positions[i * 3 + 1], p.Positions[i * 3 + 2]), transform);
                var normal = Vector3.TransformNormal(new Vector3(p.Normals[i * 3], p.Normals[i * 3 + 1], p.Normals[i * 3 + 2]), transform);
                normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY;
                // glTF's v runs down from the image top; X-Plane's t runs up from the bottom (it flips DDS on load).
                _vertices.Append("VT ").Append(Num(pos.X)).Append(' ').Append(Num(pos.Y)).Append(' ').Append(Num(pos.Z)).Append(' ')
                    .Append(Num(normal.X)).Append(' ').Append(Num(normal.Y)).Append(' ').Append(Num(normal.Z)).Append(' ')
                    .Append(Num(p.Uvs[i * 2])).Append(' ').Append(Num(1 - p.Uvs[i * 2 + 1])).Append('\n');
                VertexCount++;
            }

            // X-Plane fronts are clockwise. Asobo's exporter already writes clockwise (DirectX style), against the glTF
            // standard; other exporters may not, and some models mix both (FSLTL's E170/E190 windows). So each triangle
            // faces where its own vertex normals point, and the primitive's majority decides when they are unclear.
            // A mirroring node transform (negative scale) turns the winding over as well.
            var mirrored = transform.GetDeterminant() < 0;
            var majority = IsCounterClockwise(p);
            var start = _indices.Count;
            for (var i = 0; i + 2 < p.Indices.Length; i += 3)
            {
                var agreement = Agreement(p, i);
                var reverse = (Math.Abs(agreement) > 1e-9 ? agreement > 0 : majority) ^ mirrored;
                _indices.Add(baseVertex + p.Indices[i]);
                _indices.Add(baseVertex + p.Indices[reverse ? i + 2 : i + 1]);
                _indices.Add(baseVertex + p.Indices[reverse ? i + 1 : i + 2]);
            }

            var count = _indices.Count - start;
            Triangles += count / 3;
            if (_trisStart >= 0 && _trisStart + _trisCount == start)
            {
                _trisCount += count;
            }
            else
            {
                Flush();
                _trisStart = start;
                _trisCount = count;
            }
        }

        private void Flush()
        {
            if (_trisStart >= 0 && _trisCount > 0) _commands.Add($"TRIS {_trisStart} {_trisCount}");
            _trisStart = -1;
            _trisCount = 0;
        }

        public string Text(bool lit)
        {
            Flush();
            var sb = new StringBuilder();
            sb.Append("I\n800\nOBJ\n\n");
            sb.Append("TEXTURE {TEXTURE}\n");
            if (lit) sb.Append("TEXTURE_LIT {TEXTURE_LIT}\n");
            sb.Append(CultureInfo.InvariantCulture, $"POINT_COUNTS {VertexCount} 0 0 {_indices.Count}\n\n");
            sb.Append(_vertices).Append('\n');
            var i = 0;
            for (; i + 10 <= _indices.Count; i += 10)
            {
                sb.Append("IDX10");
                for (var k = 0; k < 10; k++) sb.Append(' ').Append(_indices[i + k]);
                sb.Append('\n');
            }

            for (; i < _indices.Count; i++) sb.Append("IDX ").Append(_indices[i]).Append('\n');
            sb.Append('\n');
            foreach (var c in _commands) sb.Append(c).Append('\n');
            return sb.ToString();
        }
    }
}

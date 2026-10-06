using System.Globalization;
using System.Numerics;
using System.Text;

namespace ClearanceLink.Fsltl;

/// <summary>An X-Plane named airplane light, as Bluebell's CSL models use them.</summary>
public enum LightKind
{
    Beacon,
    Strobe,
    NavLeft,
    NavRight,
    NavTail,
    Landing,
    Taxi,
}

/// <param name="Position">Where it sits in the converted .obj: metres, X right, Y up, nose towards -Z.</param>
public sealed record ModelLight(LightKind Kind, Vector3 Position);

/// <summary>
/// The external lights of an FSLTL aircraft, from the <c>[LIGHTS]</c> section of its systems.cfg, written as a lights-only
/// .obj that XPMP2 draws with the model. MSFS draws these lights from the cfg, not from the glTF, so a converted
/// model without them is dark at night (reported 2026-10-06).
///
/// Each line is <c>lightdef.N = Type:T#Index:i#LocalPosition:x,y,z#LocalRotation:p,b,h#EffectFile:...#Node:name#...</c>:
/// x forward, y right, z up, in feet from the reference datum. Every FSLTL base model (65 in FSLTL 1.5.0) has one,
/// with the datum at the glTF origin. A few lights (mostly nose gear landing and taxi lights) leave LocalPosition
/// empty and name a glTF node instead.
/// </summary>
public static class ModelLights
{
    private const float FeetToMetres = 0.3048f;

    /// <summary>The lights of the aircraft folder holding the model XML (its systems.cfg, else aircraft.cfg).</summary>
    public static List<ModelLight> Load(string modelXml, GltfModel model)
    {
        var aircraft = Path.GetDirectoryName(Path.GetDirectoryName(modelXml));
        if (aircraft is null) return [];
        var cfg = FileLookup.Child(aircraft, "systems.cfg") ?? FileLookup.Child(aircraft, "aircraft.cfg");
        if (cfg is null) return [];
        var datum = FileLookup.Child(aircraft, "flight_model.cfg") is { } fm ? Datum(CfgFile.Load(fm)) : Vector3.Zero;
        return Parse(CfgFile.Load(cfg), datum, name => NodePosition(model, name));
    }

    /// <summary>
    /// The beacon, strobe, nav, landing and taxi lights of a [LIGHTS] section. Logo, wing, cabin and panel lights are left
    /// out: they do not help to see an aircraft from afar. Lines that cannot be read are skipped.
    /// </summary>
    /// <param name="datum">The reference datum, in cfg axes and feet from the model origin.</param>
    /// <param name="node">Where a glTF node sits in the converted .obj, or null when the model has no such node.</param>
    public static List<ModelLight> Parse(CfgFile cfg, Vector3 datum, Func<string, Vector3?> node)
    {
        var lights = new List<ModelLight>();
        for (var i = 0; i < 1000; i++)
        {
            if (cfg.Get("LIGHTS", $"lightdef.{i}") is not { Length: > 0 } line) continue;
            var fields = Fields(line);
            if (!fields.TryGetValue("Type", out var typeText) || !int.TryParse(typeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var type)) continue;
            Vector3? at = Vector(fields.GetValueOrDefault("LocalPosition")) is { } local
                ? ToObj(local + datum)
                : fields.GetValueOrDefault("Node") is { Length: > 0 } name ? node(name) : null;
            if (at is not { } position || Kind(type, fields.GetValueOrDefault("EffectFile") ?? "", position) is not { } kind) continue;
            lights.Add(new ModelLight(kind, position));
        }

        return lights;
    }

    /// <summary>MSFS light types 1 beacon, 2 strobe, 3 nav, 5 landing, 6 taxi; nav colour from the effect name (NavigationRed...), else the side.</summary>
    public static LightKind? Kind(int type, string effect, Vector3 position) => type switch
    {
        1 => LightKind.Beacon,
        2 => LightKind.Strobe,
        3 when effect.Contains("NavigationRed", StringComparison.OrdinalIgnoreCase) => LightKind.NavLeft,
        3 when effect.Contains("NavigationGreen", StringComparison.OrdinalIgnoreCase) => LightKind.NavRight,
        3 when effect.Contains("NavigationWhite", StringComparison.OrdinalIgnoreCase) => LightKind.NavTail,
        3 => position.X < -0.5f ? LightKind.NavLeft : position.X > 0.5f ? LightKind.NavRight : LightKind.NavTail,
        5 => LightKind.Landing,
        6 => LightKind.Taxi,
        _ => null,
    };

    /// <summary>
    /// A lights-only .obj: each light shows while its XPMP2 dataref is on, the way Bluebell's models do it. No spill
    /// lights (each one lights the ground around it at a cost on the GPU, times every aircraft drawn); the named lights
    /// carry X-Plane's own flash patterns and are seen from kilometres away. Null when there are no lights.
    /// </summary>
    public static string? ObjText(IReadOnlyList<ModelLight> lights, float maxDistance = 30000)
    {
        if (lights.Count == 0) return null;
        var text = new StringBuilder("I\n800\nOBJ\n\nPOINT_COUNTS 0 0 0 0\n\n");
        text.Append("ATTR_LOD 0 ").Append(Obj8Converter.Num(maxDistance)).Append('\n');
        foreach (var group in lights.GroupBy(l => Dataref(l.Kind)))
        {
            text.Append("ANIM_begin\n")
                .Append("ANIM_show 0.5 2 ").Append(group.Key).Append('\n')
                .Append("ANIM_hide -1 0.4 ").Append(group.Key).Append('\n');
            foreach (var light in group)
            {
                var p = light.Position;
                text.Append("LIGHT_NAMED ").Append(Name(light.Kind)).Append(' ')
                    .Append(Obj8Converter.Num(p.X)).Append(' ').Append(Obj8Converter.Num(p.Y)).Append(' ').Append(Obj8Converter.Num(p.Z)).Append('\n');
            }

            text.Append("ANIM_end\n");
        }

        return text.ToString();
    }

    public static string Name(LightKind kind) => kind switch
    {
        LightKind.Beacon => "airplane_beacon",
        LightKind.Strobe => "airplane_strobe",
        LightKind.NavLeft => "airplane_nav_left",
        LightKind.NavRight => "airplane_nav_right",
        LightKind.NavTail => "airplane_nav_tail",
        LightKind.Landing => "airplane_landing",
        _ => "airplane_taxi",
    };

    private static string Dataref(LightKind kind) => kind switch
    {
        LightKind.Beacon => "libxplanemp/controls/beacon_lites_on",
        LightKind.Strobe => "libxplanemp/controls/strobe_lites_on",
        LightKind.NavLeft or LightKind.NavRight or LightKind.NavTail => "libxplanemp/controls/nav_lites_on",
        LightKind.Landing => "libxplanemp/controls/landing_lites_on",
        _ => "libxplanemp/controls/taxi_lites_on",
    };

    /// <summary>cfg axes (feet; x forward, y right, z up) to the converted .obj (metres; X right, Y up, nose -Z).</summary>
    public static Vector3 ToObj(Vector3 cfg) => new(cfg.Y * FeetToMetres, cfg.Z * FeetToMetres, -cfg.X * FeetToMetres);

    /// <summary>
    /// A node's place in the converted .obj with every node at its rest pose (the gear as modelled, not animated),
    /// or null when the model has no node of that name.
    /// </summary>
    public static Vector3? NodePosition(GltfModel model, string name)
    {
        Vector3? found = null;
        void Walk(int index, Matrix4x4 parent)
        {
            if (found is not null) return;
            var node = model.Nodes[index];
            var world = Obj8Converter.Local(node) * parent;
            if (string.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                // glTF: X to the left wing, Y up, Z forward; the .obj turns that half a turn about Y.
                var t = world.Translation;
                found = new Vector3(-t.X, t.Y, -t.Z);
                return;
            }

            foreach (var c in node.Children) Walk(c, world);
        }

        foreach (var root in model.Roots) Walk(root, Matrix4x4.Identity);
        return found;
    }

    private static Vector3 Datum(CfgFile flightModel) =>
        Vector(flightModel.Get("WEIGHT_AND_BALANCE", "reference_datum_position")) ?? Vector3.Zero;

    private static Dictionary<string, string> Fields(string line)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in line.Split('#'))
        {
            var colon = part.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0) fields[part[..colon].Trim()] = part[(colon + 1)..].Trim();
        }

        return fields;
    }

    /// <summary>"x, y, z" in any spacing; null when empty or not three finite numbers.</summary>
    private static Vector3? Vector(string? text)
    {
        var parts = (text ?? "").Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 3) return null;
        var v = new float[3];
        for (var i = 0; i < 3; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || !float.IsFinite(v[i])) return null;
        }

        return new Vector3(v[0], v[1], v[2]);
    }
}

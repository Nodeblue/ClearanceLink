using System.Globalization;
using System.Xml.Linq;

namespace XPConnect.Fsltl;

/// <summary>What an MSFS animation moves, and the XPMP2 dataref that drives it in X-Plane.</summary>
/// <param name="Dataref">XPMP2 dataref, or null to pose the part and leave it still.</param>
/// <param name="Scale">Dataref value at the end of the animation (the start is 0).</param>
/// <param name="Offset">Dataref value at the start of the animation.</param>
/// <param name="PoseRatio">Where a still part is posed, as a share of the animation's length.</param>
/// <param name="PoseFrame">Where a still part is posed, as a frame of <paramref name="PoseLength"/>; wins over <paramref name="PoseRatio"/>.</param>
/// <param name="PoseLength">The template's ANIM_LENGTH, which MSFS stretches over the animation's keys.</param>
public sealed record AnimationRole(string Kind, string? Dataref, float Scale = 1, float Offset = 0, float PoseRatio = 0, float? PoseFrame = null, float PoseLength = 100)
{
    public static readonly AnimationRole Still = new("still", null);

    public float Value(float ratio) => Offset + ratio * Scale;
}

/// <summary>
/// Reads a model XML's behaviours (<c>&lt;UseTemplate&gt;</c> with <c>&lt;ANIM_NAME&gt;</c>) and decides, per glTF animation
/// name, what it animates. FSLTL models name animations after whatever the author picked (e.g. flaps on
/// <c>custom_anim_SIM_ON_GROUND_05</c>), so the template name, its parameters, component ID and ANIM_CODE decide, not
/// the name. The FSLTL templates are defined in the package's ModelBehaviorDefs/FSLTL/Misc/Aircraft.xml.
/// </summary>
public static class AnimationRoles
{
    public const string GearRatio = "libxplanemp/controls/gear_ratio";
    public const string FlapRatio = "libxplanemp/controls/flap_ratio";
    public const string SlatRatio = "libxplanemp/controls/slat_ratio";
    public const string SpoilerRatio = "libxplanemp/controls/spoiler_ratio";
    public const string ReverserRatio = "libxplanemp/engines/thrust_reverser_deploy_ratio";
    public const string TireRotation = "libxplanemp/gear/tire_rotation_angle_deg";
    public const string EngineAngle = "libxplanemp/engines/engine_rotation_angle_deg1";

    /// <summary>XPMP2's nose wheel angle, in degrees despite its name.</summary>
    public const string NoseWheelAngle = "libxplanemp/controls/nws_ratio";

    /// <summary>An engine part that MSFS shows or hides by engine speed (FSLTL_Engine_Blur / FSLTL_Engine_Static).</summary>
    public enum EnginePart
    {
        Blur,
        Still,
    }

    public static Dictionary<string, AnimationRole> Load(string xmlPath)
    {
        var roles = new Dictionary<string, AnimationRole>(StringComparer.OrdinalIgnoreCase);
        foreach (var use in TemplateUses(xmlPath))
        {
            var animName = Child(use, "ANIM_NAME");
            if (string.IsNullOrWhiteSpace(animName)) continue;
            var component = use.Ancestors().FirstOrDefault(a => a.Name.LocalName == "Component")?.Attribute("ID")?.Value ?? "";
            roles[animName.Trim()] = Classify(use.Attribute("Name")?.Value ?? "", component, Child(use, "ANIM_CODE") ?? "", name => Child(use, name));
        }

        return roles;
    }

    /// <summary>Nodes the engine templates show or hide by engine speed (their NODE_NAME parameter).</summary>
    public static Dictionary<string, EnginePart> EngineParts(string xmlPath)
    {
        var parts = new Dictionary<string, EnginePart>(StringComparer.OrdinalIgnoreCase);
        foreach (var use in TemplateUses(xmlPath))
        {
            var template = use.Attribute("Name")?.Value ?? "";
            var node = Child(use, "NODE_NAME")?.Trim();
            if (string.IsNullOrEmpty(node)) continue;
            if (template.Equals("FSLTL_Engine_Blur", StringComparison.OrdinalIgnoreCase)) parts[node] = EnginePart.Blur;
            if (template.Equals("FSLTL_Engine_Static", StringComparison.OrdinalIgnoreCase)) parts[node] = EnginePart.Still;
        }

        return parts;
    }

    /// <summary>
    /// An engine part from an animation or node name, for models whose XML does not say (TFS's Dash 8 uses legacy
    /// "prop0_blurred" / "prop0_still"). Wheel animations called "..._tire_blurred_key" are not engine parts.
    /// </summary>
    public static EnginePart? EnginePartFromName(string name)
    {
        bool Has(string word) => name.Contains(word, StringComparison.OrdinalIgnoreCase);
        if (Has("tire") || Has("tyre") || Has("wheel")) return null;
        // "slow" is the part-speed disc between still blades and full blur (TFS's Dash 8 prop0_slow).
        if (Has("blur") || ((Has("prop") || Has("fan")) && Has("slow"))) return EnginePart.Blur;
        if ((Has("prop") || Has("fan") || Has("N1_")) && (Has("still") || Has("static"))) return EnginePart.Still;
        return null;
    }

    /// <summary>The role for one template use; <see cref="AnimationRole.Still"/> when it is nothing XPMP2 can drive.</summary>
    /// <param name="parameter">The use's template parameters (e.g. LEGACY_PARAM_2), null when not given.</param>
    public static AnimationRole Classify(string template, string component, string code, Func<string, string?>? parameter = null)
    {
        bool Has(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);
        float Param(string name, float fallback) =>
            parameter?.Invoke(name) is { } text && float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        if (Has(template, "GEAR") && !Has(template, "DOOR")) return new AnimationRole("gear", GearRatio);
        // FSLTL_Engine_Static turns the still blades a full turn over its length; XPMP2's engine angle turns them.
        if (template.Equals("FSLTL_Engine_Static", StringComparison.OrdinalIgnoreCase)) return new AnimationRole("propeller", EngineAngle, Scale: 360);
        if (Has(template, "Wheel_Roll") || Has(template, "WHEEL_ROTATION")) return new AnimationRole("wheel", TireRotation, Scale: 360);
        // FSLTL_Steering's frame is (yaw rate * P1 + P2) * 10 + OFFSET, so straight ahead is frame P2 * 10 + OFFSET;
        // FSLTL_Alt_Steering's is |yaw rate * SHARP + START|, straight at START.
        if (template.Equals("FSLTL_Alt_Steering", StringComparison.OrdinalIgnoreCase)) return new AnimationRole("steering", NoseWheelAngle, PoseFrame: MathF.Abs(Param("STEER_START", 0)), PoseLength: Param("ANIM_LENGTH", 100));
        if (Has(template, "Steering")) return new AnimationRole("steering", NoseWheelAngle, PoseFrame: Param("LEGACY_PARAM_2", 50) * 10 + Param("OFFSET", 0), PoseLength: Param("ANIM_LENGTH", 1000));
        // Parked with the beacon off: FSLTL_Elevator_Droop is at its end (perc_down), FSLTL_Elevator_Up at its start.
        if (template.Equals("FSLTL_Elevator_Droop", StringComparison.OrdinalIgnoreCase)) return new AnimationRole("elevator", null, PoseRatio: 1);
        if (Has(component, "Slat") || Has(code, "LEADING EDGE FLAPS")) return new AnimationRole("slats", SlatRatio);
        if (Has(component, "Flap") || Has(code, "TRAILING EDGE FLAPS")) return new AnimationRole("flaps", FlapRatio);
        if (Has(component, "Spoiler") || Has(code, "SPOILERS")) return new AnimationRole("spoilers", SpoilerRatio);
        if (Has(component, "Revers")) return new AnimationRole("reverser", ReverserRatio);
        return AnimationRole.Still;
    }

    /// <summary>
    /// The role of an animation the model XML does not mention (driven from an included behaviour file), from its name:
    /// FSLTL's E190 calls its gear "main_gear_l". Gear doors move with the gear, so they get the gear dataref too.
    /// </summary>
    public static AnimationRole FromName(string name)
    {
        bool Has(string word) => name.Contains(word, StringComparison.OrdinalIgnoreCase);
        if (Has("tire") || Has("tyre") || Has("wheel_rot") || Has("WHEEL_ROTATION")) return new AnimationRole("wheel", TireRotation, Scale: 360);
        if (Has("gear") && !Has("steer")) return new AnimationRole("gear", GearRatio);
        if (EnginePartFromName(name) == EnginePart.Still) return new AnimationRole("propeller", EngineAngle, Scale: 360);
        return AnimationRole.Still;
    }

    private static List<XElement> TemplateUses(string xmlPath)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(xmlPath);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            return [];
        }

        return doc.Descendants().Where(e => e.Name.LocalName == "UseTemplate").ToList();
    }

    /// <summary>A template parameter, matched case-insensitively (FSLTL writes both perc_down and PERC_DOWN).</summary>
    private static string? Child(XElement e, string name) =>
        e.Elements().FirstOrDefault(c => c.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
}

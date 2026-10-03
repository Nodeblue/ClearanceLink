using System.Xml.Linq;

namespace XPConnect.Fsltl;

/// <summary>What an MSFS animation moves, and the XPMP2 dataref that drives it in X-Plane.</summary>
/// <param name="Dataref">XPMP2 dataref, or null to pose the part at <paramref name="PoseRatio"/> and leave it still.</param>
/// <param name="Scale">Dataref value at the end of the animation (the start is 0).</param>
/// <param name="Offset">Dataref value at the start of the animation.</param>
public sealed record AnimationRole(string Kind, string? Dataref, float Scale = 1, float Offset = 0, float PoseRatio = 0)
{
    public static readonly AnimationRole Still = new("still", null);

    public float Value(float ratio) => Offset + ratio * Scale;
}

/// <summary>
/// Reads a model XML's behaviours (<c>&lt;UseTemplate&gt;</c> with <c>&lt;ANIM_NAME&gt;</c>) and decides, per glTF animation
/// name, what it animates. FSLTL models name animations after whatever the author picked (e.g. flaps on
/// <c>custom_anim_SIM_ON_GROUND_05</c>), so the template name, component ID and ANIM_CODE decide, not the name.
/// </summary>
public static class AnimationRoles
{
    public const string GearRatio = "libxplanemp/controls/gear_ratio";
    public const string FlapRatio = "libxplanemp/controls/flap_ratio";
    public const string SlatRatio = "libxplanemp/controls/slat_ratio";
    public const string SpoilerRatio = "libxplanemp/controls/spoiler_ratio";
    public const string ReverserRatio = "libxplanemp/engines/thrust_reverser_deploy_ratio";
    public const string TireRotation = "libxplanemp/gear/tire_rotation_angle_deg";

    public static Dictionary<string, AnimationRole> Load(string xmlPath)
    {
        var roles = new Dictionary<string, AnimationRole>(StringComparer.OrdinalIgnoreCase);
        XDocument doc;
        try
        {
            doc = XDocument.Load(xmlPath);
        }
        catch (System.Xml.XmlException)
        {
            return roles;
        }

        foreach (var use in doc.Descendants().Where(e => e.Name.LocalName == "UseTemplate"))
        {
            var animName = Child(use, "ANIM_NAME");
            if (string.IsNullOrWhiteSpace(animName)) continue;
            var component = use.Ancestors().FirstOrDefault(a => a.Name.LocalName == "Component")?.Attribute("ID")?.Value ?? "";
            roles[animName.Trim()] = Classify(use.Attribute("Name")?.Value ?? "", component, Child(use, "ANIM_CODE") ?? "");
        }

        return roles;
    }

    /// <summary>The role for one template use; <see cref="AnimationRole.Still"/> when it is nothing XPMP2 can drive.</summary>
    public static AnimationRole Classify(string template, string component, string code)
    {
        bool Has(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);
        if (Has(template, "GEAR") && !Has(template, "DOOR")) return new AnimationRole("gear", GearRatio);
        if (Has(template, "Wheel_Roll") || Has(template, "WHEEL_ROTATION")) return new AnimationRole("wheel", TireRotation, Scale: 360);
        if (Has(template, "Steering")) return new AnimationRole("steering", null, PoseRatio: 0.5f);
        if (Has(component, "Slat") || Has(code, "LEADING EDGE FLAPS")) return new AnimationRole("slats", SlatRatio);
        if (Has(component, "Flap") || Has(code, "TRAILING EDGE FLAPS")) return new AnimationRole("flaps", FlapRatio);
        if (Has(component, "Spoiler") || Has(code, "SPOILERS")) return new AnimationRole("spoilers", SpoilerRatio);
        if (Has(component, "Revers")) return new AnimationRole("reverser", ReverserRatio);
        return AnimationRole.Still;
    }

    private static string? Child(XElement e, string name) => e.Elements().FirstOrDefault(c => c.Name.LocalName == name)?.Value;
}

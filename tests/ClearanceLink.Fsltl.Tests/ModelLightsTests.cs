using System.Numerics;

namespace ClearanceLink.Fsltl.Tests;

public sealed class ModelLightsTests
{
    // FSLTL 1.5.0's FSLTL_A320 systems.cfg, as read on Ben's PC on 2026-10-06 (logo lights and EmMesh fields left out).
    private const string A320 = """
        [LIGHTS]
        lightdef.0 = Type:1#Index:0#LocalPosition:-7.6,0.00,-6.0#LocalRotation:0,0,0#EffectFile:LIGHT_FSLTL_A320_BeaconBelly#Node:#PotentiometerIndex:1
        lightdef.2 = Type:2#Index:0#LocalPosition:-69.35232, 0, 4.27876#LocalRotation:0,0,180#EffectFile:LIGHT_FSLTL_A320_StrobeSimple#Node:#PotentiometerIndex:24
        lightdef.3 = Type:3#Index:0#LocalPosition:-69.35232, 0, 4.372896#LocalRotation:0,0,180#EffectFile:LIGHT_FSLTL_A320_NavigationWhite#Node:#PotentiometerIndex:1
        lightdef.6 = Type:3#Index:0#LocalPosition:-13.6,-55.1,3.0#LocalRotation:0,0,0#EffectFile:LIGHT_FSLTL_A320_NavigationRed#Node:#PotentiometerIndex:1
        lightdef.7 = Type:3#Index:0#LocalPosition:-13.6,55.1,3.0#LocalRotation:0,0,0#EffectFile:LIGHT_FSLTL_A320_NavigationGreen#Node:#PotentiometerIndex:1
        lightdef.8 = Type:5#Index:0#LocalPosition:0.08, -7.025432, -3.701152#LocalRotation:5,0,10#EffectFile:LIGHT_FSLTL_A320_LandingLarge#Node:#PotentiometerIndex:1
        lightdef.10 = Type:5#Index:0#LocalPosition:#LocalRotation:#EffectFile:LIGHT_FSLTL_A320_TakeOff#Node:takeoff#PotentiometerIndex:1
        lightdef.11 = Type:6#Index:0#LocalPosition:37.143, 0.7171, -5.438#LocalRotation:0,0,0#EffectFile:LIGHT_FSLTL_A320_TaxiLarge#Node:#PotentiometerIndex:1
        lightdef.16 = Type:9#Index:1#LocalPosition:-61.8,-11.8,5.15#LocalRotation:-58,0,-70#EffectFile:LIGHT_FSLTL_A320_LogoLightLarge#PotentiometerIndex:1
        """;

    [Fact]
    public void Lightdefs_become_xplane_lights_in_obj_axes()
    {
        var lights = ModelLights.Parse(CfgFile.Parse(A320), Vector3.Zero, name => name == "takeoff" ? new Vector3(-0.2f, -1.3f, -11.4f) : null);

        Assert.Equal(
            [LightKind.Beacon, LightKind.Strobe, LightKind.NavTail, LightKind.NavLeft, LightKind.NavRight, LightKind.Landing, LightKind.Landing, LightKind.Taxi],
            lights.Select(l => l.Kind));
        // Red nav on the left wing tip (X negative), behind the nose (Z positive), above the datum.
        var red = lights.Single(l => l.Kind == LightKind.NavLeft).Position;
        Assert.Equal(-16.794f, red.X, 3);
        Assert.Equal(0.914f, red.Y, 3);
        Assert.Equal(4.145f, red.Z, 3);
        // The tail light 21 m behind the datum; the taxi light 11 m ahead of it (nose towards -Z).
        Assert.Equal(21.139f, lights.Single(l => l.Kind == LightKind.NavTail).Position.Z, 3);
        Assert.Equal(-11.321f, lights.Single(l => l.Kind == LightKind.Taxi).Position.Z, 3);
        Assert.Equal(new Vector3(-0.2f, -1.3f, -11.4f), lights[6].Position);
    }

    [Fact]
    public void Broken_and_unplaced_lines_are_skipped()
    {
        var cfg = CfgFile.Parse("""
            [LIGHTS]
            lightdef.0 =
            lightdef.1 = Type:x#LocalPosition:1,2,3
            lightdef.2 = Type:3#LocalPosition:1,2#EffectFile:LIGHT_Navigationred
            lightdef.3 = Type:2#LocalPosition:#Node:missing
            lightdef.4 = Type: 2 #LocalPosition: 1 , 2 , 3 #EffectFile:LIGHT_Newlight_Strobe
            """);

        var light = Assert.Single(ModelLights.Parse(cfg, Vector3.Zero, _ => null));
        Assert.Equal(LightKind.Strobe, light.Kind);
    }

    [Theory]
    [InlineData("LIGHT_FSLTL_Navigationred", 5f, LightKind.NavLeft)]
    [InlineData("LIGHT_FSLTL_B737_NavigationWhite", 16f, LightKind.NavTail)]
    [InlineData("LIGHT_ASOBO_Nav", -16f, LightKind.NavLeft)]
    [InlineData("LIGHT_ASOBO_Nav", 16f, LightKind.NavRight)]
    [InlineData("LIGHT_ASOBO_Nav", 0f, LightKind.NavTail)]
    public void Nav_colour_comes_from_the_effect_else_the_side(string effect, float x, LightKind kind) =>
        Assert.Equal(kind, ModelLights.Kind(3, effect, new Vector3(x, 0, 0)));

    [Fact]
    public void Lights_obj_shows_each_group_on_its_xpmp2_dataref()
    {
        var text = ModelLights.ObjText([new ModelLight(LightKind.NavLeft, new Vector3(-16.5f, 1, 4)), new ModelLight(LightKind.NavRight, new Vector3(16.5f, 1, 4)), new ModelLight(LightKind.Strobe, new Vector3(0, 1, 21))]);

        Assert.Equal("""
            I
            800
            OBJ

            POINT_COUNTS 0 0 0 0

            ATTR_LOD 0 30000
            ANIM_begin
            ANIM_show 0.5 2 libxplanemp/controls/nav_lites_on
            ANIM_hide -1 0.4 libxplanemp/controls/nav_lites_on
            LIGHT_NAMED airplane_nav_left -16.5 1 4
            LIGHT_NAMED airplane_nav_right 16.5 1 4
            ANIM_end
            ANIM_begin
            ANIM_show 0.5 2 libxplanemp/controls/strobe_lites_on
            ANIM_hide -1 0.4 libxplanemp/controls/strobe_lites_on
            LIGHT_NAMED airplane_strobe 0 1 21
            ANIM_end

            """.ReplaceLineEndings("\n"), text);
        Assert.Null(ModelLights.ObjText([]));
    }

    [Fact]
    public void Datum_offset_moves_cfg_positions()
    {
        var cfg = CfgFile.Parse("[LIGHTS]\nlightdef.0 = Type:1#LocalPosition:0,0,0\n");
        var light = Assert.Single(ModelLights.Parse(cfg, new Vector3(10, 0, 0), _ => null));
        Assert.Equal(-3.048f, light.Position.Z, 3);
    }
}

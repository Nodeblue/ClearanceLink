using XPConnect.Protocol;
using XPConnect.Server;

namespace XPConnect.Server.Tests;

public sealed class MappingTests
{
    // The two blocks BATC needs to show "connected" (recorded 2026-09-30): the helper's define 300 and BATC's define 17.
    public static TheoryData<int, uint> ConnectedBlocks => new() { { 1, 300 }, { 2, 17 } };

    [Theory]
    [MemberData(nameof(ConnectedBlocks))]
    public void Every_simvar_in_batcs_core_blocks_is_mapped(int connection, uint defineId)
    {
        var mappings = Fixtures.Mappings();
        var datums = Fixtures.ClientPackets(connection)
            .Where(p => p.FunctionId == 0x0C)
            .Select(PacketDecoder.Decode)
            .Where(f => (uint)f["defineId"]! == defineId)
            .Select(f => (string)f["datumName"]!)
            .ToList();

        Assert.NotEmpty(datums);
        var unmapped = datums.Where(d => mappings.FindSimVar(d, out _) is null).ToList();
        Assert.Empty(unmapped);
    }

    [Fact]
    public void Mapping_units_and_derivations_are_known()
    {
        var mappings = Fixtures.Mappings();
        Assert.NotEmpty(mappings.SimVars);
        Assert.Empty(mappings.SimVars.Where(m => !Units.IsKnown(m.Units)).Select(m => $"{m.Name}: {m.Units}"));
        Assert.Empty(mappings.Events.Where(m => !Units.IsKnown(m.FromUnits) || !Units.IsKnown(m.ToUnits)).Select(m => m.Name));
        Assert.Empty(mappings.SimVars.Where(m => m.Dataref is null && m.Derive is null && m.Const is null && m.ConstText is null).Select(m => m.Name));

        var xp = Fixtures.ParkedAtYbbn();
        var resolver = new SimVarResolver(mappings, xp);
        foreach (var map in mappings.SimVars.Where(m => m.Derive is not null))
        {
            // Throws for an unknown derivation name; missing inputs just leave it pending.
            resolver.Resolve(map.Indexed ? map.Name + ":1" : map.Name, map.Units);
        }
    }

    [Fact]
    public void Indexed_simvar_expands_dataref_and_converts_units()
    {
        var resolver = new SimVarResolver(Fixtures.Mappings(), Fixtures.ParkedAtYbbn());

        Assert.Equal((SimValue.Of(124.85), ResolveStatus.Ok), Round(resolver.Resolve("COM ACTIVE FREQUENCY:2", "Mhz")));
        Assert.Equal(1.0, resolver.Resolve("COM TRANSMIT:1", "Bool").Value.Number);
        Assert.Equal(0.0, resolver.Resolve("COM TRANSMIT:2", "Bool").Value.Number);
        Assert.Equal(1200.0, resolver.Resolve("TRANSPONDER CODE:1", "").Value.Number);
        Assert.Equal(0x1200, resolver.Resolve("TRANSPONDER CODE:1", "bco16").Value.Number);
        Assert.Equal(-1.5, resolver.Resolve("PLANE PITCH DEGREES", "degrees").Value.Number);
        Assert.Equal("SF50", resolver.Resolve("ATC MODEL", "").Value.Text);
    }

    [Fact]
    public void Ground_altitude_is_elevation_minus_height_in_feet()
    {
        var (value, status) = new SimVarResolver(Fixtures.Mappings(), Fixtures.ParkedAtYbbn()).Resolve("GROUND ALTITUDE", "feet");

        Assert.Equal(ResolveStatus.Ok, status);
        Assert.Equal((4.67 - 0.87) / 0.3048, value.Number, 6);
    }

    [Fact]
    public void Calibrated_altitude_matches_msfs_at_ybbn()
    {
        // MSFS sent INDICATED ALTITUDE CALIBRATED 15.31 ft with PRESSURE ALTITUDE -358.16 ft at YBBN (QNH ~1027 hPa).
        var (value, status) = new SimVarResolver(Fixtures.Mappings(), Fixtures.ParkedAtYbbn()).Resolve("INDICATED ALTITUDE CALIBRATED", "feet");

        Assert.Equal(ResolveStatus.Ok, status);
        Assert.InRange(value.Number, 10, 20);
    }

    [Theory]
    [InlineData(0, 1013.25, 0)]
    [InlineData(7000, 1013.25, 7000)]
    [InlineData(0, 1023.25, 273)] // about 27 ft per hPa near sea level
    public void Qnh_altitude_follows_the_standard_atmosphere(double pressureAltitude, double qnh, double expected) =>
        Assert.InRange(Derivations.QnhAltitudeFeet(pressureAltitude, qnh), expected - 30, expected + 30);

    [Theory]
    [InlineData(10114.84, 30.33, 30.33, 10114.84)]   // QNH set: exactly the cockpit reading
    [InlineData(10114.84, 30.33, 30.3026, 10090.6)]  // X-Plane 12.4.3 in the climb out of YBBN, 2026-09-30
    [InlineData(-207.93, 29.92, 30.3026, 143.0)]     // baro left at standard on the ground
    public void Altimeter_is_re_referenced_to_the_qnh(double indicated, double settingInHg, double qnhInHg, double expected) =>
        Assert.InRange(
            Derivations.AltimeterAtQnhFeet(indicated, settingInHg * 33.8638866667, qnhInHg * 33.8638866667),
            expected - 3,
            expected + 3);
    [Fact]
    public void Parked_airspeed_is_never_negative()
    {
        var xp = Fixtures.ParkedAtYbbn();
        xp["sim/flightmodel/position/indicated_airspeed"] = -7.2;

        Assert.Equal(0.0, new SimVarResolver(Fixtures.Mappings(), xp).Resolve("AIRSPEED INDICATED", "knots").Value.Number);
    }

    [Fact]
    public void Missing_dataref_is_pending_and_unknown_simvar_is_unmapped()
    {
        var resolver = new SimVarResolver(Fixtures.Mappings(), new InMemoryXPlane());

        Assert.Equal(ResolveStatus.Pending, resolver.Resolve("PLANE LATITUDE", "degrees").Status);
        Assert.Equal(ResolveStatus.Unmapped, resolver.Resolve("NOT A SIMVAR", "number").Status);
    }

    [Theory]
    [InlineData(1200, 0x1200)]
    [InlineData(7700, 0x7700)]
    [InlineData(4521, 0x4521)]
    public void Transponder_codes_round_trip_through_bco16(int code, int bco)
    {
        Assert.Equal(bco, Units.Convert(code, "number", "bco16"));
        Assert.Equal(code, Units.Convert(bco, "bco16", "number"));
    }

    private static (SimValue, ResolveStatus) Round((SimValue Value, ResolveStatus Status) r) =>
        (SimValue.Of(Math.Round(r.Value.Number, 6)), r.Status);
}

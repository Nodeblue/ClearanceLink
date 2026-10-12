using ClearanceLink.Protocol;

namespace ClearanceLink.Protocol.Tests;

public class DataDefinitionTests
{
    [Fact]
    public void Packs_doubles_like_the_recorded_msfs_reply()
    {
        // First five FLOAT64 entries of the ATC app's define 17 as MSFS 2024 sent them (recording 20260930-174605).
        const string recorded = "189696C7CF663BC0ABAB350097236340F7FFFFFF2B9F2E40FAFFFFFF916276C07525F1F548B92E40";
        var values = new Dictionary<string, object?>
        {
            ["PLANE LATITUDE"] = -27.401607965721354,
            ["PLANE LONGITUDE"] = 153.11218271715674,
            ["INDICATED ALTITUDE CALIBRATED"] = 15.310882568359359,
            ["PRESSURE ALTITUDE"] = -358.16064453124966,
            ["PLANE ALTITUDE"] = 15.361884771048361,
        };
        var definition = new DataDefinition();
        foreach (var name in values.Keys)
        {
            definition.Add(new Datum(name, "degrees", 4));
        }

        var block = definition.Pack(d => values[d.Name]);

        Assert.Equal(recorded, Convert.ToHexString(block));
    }

    [Fact]
    public void Packs_mixed_types_in_order_without_padding()
    {
        var definition = new DataDefinition();
        definition.Add(new Datum("SIM ON GROUND", "", 1));
        definition.Add(new Datum("COM VOLUME:1", "", 3));
        definition.Add(new Datum("ATC ID", "", 5));
        definition.Add(new Datum("ATC MODEL", "", 9));
        definition.Add(new Datum("UNKNOWN VAR", "", 4));
        definition.Add(new Datum("Initial Position", "", 12));

        var block = definition.Pack(d => d.Name switch
        {
            "SIM ON GROUND" => 1.0,
            "COM VOLUME:1" => 1.0,
            "ATC ID" => "VH-LONGREG",
            "ATC MODEL" => "SF50",
            _ => null,
        });

        Assert.Equal(4 + 4 + 8 + 256 + 8 + 56, block.Length);
        Assert.Equal("01000000", Convert.ToHexString(block.AsSpan(0, 4)));
        Assert.Equal("0000803F", Convert.ToHexString(block.AsSpan(4, 4)));
        Assert.Equal("56482D4C4F4E4700", Convert.ToHexString(block.AsSpan(8, 8))); // truncated, NUL-terminated
        Assert.Equal("5346353000", Convert.ToHexString(block.AsSpan(16, 5)));
        Assert.All(block.AsSpan(272).ToArray(), b => Assert.Equal(0, b));
    }
}

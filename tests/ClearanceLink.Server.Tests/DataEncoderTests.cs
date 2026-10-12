using ClearanceLink.Server;

namespace ClearanceLink.Server.Tests;

public sealed class DataEncoderTests
{
    [Fact]
    public void Float64_block_matches_the_recorded_msfs_reply()
    {
        // First five FLOAT64 entries of the ATC app's define 17 as MSFS 2024 sent them (recording 20260930-174605),
        // the same bytes DataDefinitionTests checks for the stand-in's packer.
        const string recorded = "189696C7CF663BC0ABAB350097236340F7FFFFFF2B9F2E40FAFFFFFF916276C07525F1F548B92E40";
        double[] values = [-27.401607965721354, 153.11218271715674, 15.310882568359359, -358.16064453124966, 15.361884771048361];
        using var block = new MemoryStream();
        foreach (var value in values)
        {
            DataEncoder.Write(block, SimConnectDataType.Float64, SimValue.Of(value));
        }

        Assert.Equal(recorded, Convert.ToHexString(block.ToArray()));
    }

    [Theory]
    [InlineData(SimConnectDataType.Int32, "01000000")]
    [InlineData(SimConnectDataType.Float32, "0000803F")]
    public void Numbers_are_little_endian(SimConnectDataType type, string hex)
    {
        using var block = new MemoryStream();
        DataEncoder.Write(block, type, SimValue.Of(1));

        Assert.Equal(hex, Convert.ToHexString(block.ToArray()));
    }

    [Fact]
    public void Fixed_strings_are_truncated_and_nul_terminated()
    {
        using var block = new MemoryStream();
        DataEncoder.Write(block, SimConnectDataType.String8, SimValue.Of("VH-LONGREG"));
        DataEncoder.Write(block, SimConnectDataType.InitPosition, SimValue.Zero);

        var bytes = block.ToArray();
        Assert.Equal(8 + 56, bytes.Length);
        Assert.Equal("56482D4C4F4E4700", Convert.ToHexString(bytes, 0, 8));
    }
}

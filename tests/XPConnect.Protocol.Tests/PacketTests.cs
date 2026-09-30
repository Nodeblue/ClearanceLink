using XPConnect.Protocol;

namespace XPConnect.Protocol.Tests;

public class PacketTests
{
    [Fact]
    public void Client_open_packet_has_expected_header_and_size()
    {
        var packet = TestPackets.Open();

        // 16 header + 256 name + 4 + 1 + 3 alias + 4 x 4 version = 296
        Assert.Equal(296u, packet.Size);
        Assert.Equal(296, packet.Raw.Length);
        Assert.Equal(TestPackets.SunRise, packet.Protocol);
        Assert.Equal(0xF0000001u, packet.TypeField);
        Assert.Equal(1u, packet.FunctionId);
        Assert.Equal(1u, packet.SendId);
        Assert.Equal("Open", packet.Name);
    }

    [Fact]
    public void Decodes_open_fields()
    {
        var f = PacketDecoder.Decode(TestPackets.Open("BeyondATC"));

        Assert.Equal("BeyondATC", f["appName"]);
        Assert.Equal("RS", f["alias"]);
        Assert.Equal(12u, f["versionMajor"]);
        Assert.Equal(282174u, f["buildMajor"]);
        Assert.False(f.ContainsKey("decodeWarning"));
    }

    [Fact]
    public void Decodes_add_to_data_definition()
    {
        var f = PacketDecoder.Decode(TestPackets.AddToDataDefinition(7, "PLANE ALTITUDE", "feet"));

        Assert.Equal(7u, f["defineId"]);
        Assert.Equal("PLANE ALTITUDE", f["datumName"]);
        Assert.Equal("feet", f["unitsName"]);
        Assert.Equal(4u, f["dataType"]);
    }

    [Fact]
    public void Server_open_reply_round_trips_through_decoder()
    {
        var reply = ServerIdentity.Msfs2024.BuildOpenReply(TestPackets.SunRise);
        var f = PacketDecoder.Decode(reply);

        Assert.Equal(Direction.ServerToClient, reply.Direction);
        Assert.Equal("Open", reply.Name);
        Assert.Equal(12 + 256 + 40, reply.Raw.Length);
        Assert.Equal("SunRise", f["applicationName"]);
        Assert.Null(reply.SendId);
    }

    [Fact]
    public void Short_payload_is_flagged_not_thrown()
    {
        var truncated = PacketBuilder.Client(0x0C, TestPackets.SunRise, 1).U32(1).Build();

        var f = PacketDecoder.Decode(truncated);

        Assert.Equal("payload shorter than expected layout", f["decodeWarning"]);
    }

    [Fact]
    public void Unknown_function_ids_get_a_stable_name()
    {
        Assert.Equal("Unknown_0xFE", FunctionNames.Get(0xFE));
        Assert.Equal("RequestFacilityData", FunctionNames.Get(0x46));
        Assert.Equal("RequestAllFacilities", FunctionNames.Get(0x5E));
    }

    [Fact]
    public async Task Framer_reads_back_to_back_packets_and_clean_end()
    {
        var stream = new MemoryStream();
        await PacketFramer.WriteAsync(stream, TestPackets.Open());
        await PacketFramer.WriteAsync(stream, TestPackets.AddToDataDefinition(1, "PLANE HEADING DEGREES TRUE", "degrees"));
        stream.Position = 0;

        var first = await PacketFramer.ReadAsync(stream, Direction.ClientToServer);
        var second = await PacketFramer.ReadAsync(stream, Direction.ClientToServer);
        var end = await PacketFramer.ReadAsync(stream, Direction.ClientToServer);

        Assert.Equal("Open", first!.Name);
        Assert.Equal("AddToDataDefinition", second!.Name);
        Assert.Null(end);
    }

    [Fact]
    public async Task Framer_rejects_absurd_sizes()
    {
        var stream = new MemoryStream([0xFF, 0xFF, 0xFF, 0x7F, 0, 0, 0, 0]);

        await Assert.ThrowsAsync<InvalidDataException>(() => PacketFramer.ReadAsync(stream, Direction.ClientToServer));
    }
}

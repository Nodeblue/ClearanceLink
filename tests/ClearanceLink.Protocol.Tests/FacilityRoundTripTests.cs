using ClearanceLink.Protocol.Facilities;

namespace ClearanceLink.Protocol.Tests;

/// <summary>
/// Decodes MSFS 2024's recorded airport answers to the ATC app and re-encodes them: the encoder must reproduce every
/// packet byte for byte (recordings/samples/atc-app-1.8.21-facilities.jsonl).
/// </summary>
public sealed class FacilityRoundTripTests
{
    public static TheoryData<string, uint> Airports => new() { { "YBBN", 444 }, { "YTWB", 444 }, { "YBAF", 333 } };

    [Theory]
    [InlineData(444u, 318)]
    [InlineData(333u, 255)]
    public void The_apps_definitions_use_only_known_fields(uint defineId, int entries)
    {
        var names = FacilityFixture.DefinitionNames(defineId);
        var definition = FacilityDefinition.FromNames(names);

        Assert.Equal(entries, names.Count);
        Assert.True(definition.IsComplete);
        Assert.Empty(definition.UnknownFields);
        Assert.Equal("AIRPORT", definition.Root!.Name);
    }

    [Theory]
    [MemberData(nameof(Airports))]
    public void Encoder_reproduces_msfs_reply_byte_for_byte(string icao, uint defineId)
    {
        var definition = FacilityDefinition.FromNames(FacilityFixture.DefinitionNames(defineId)).Root!;
        var (requestId, recorded) = FacilityFixture.Reply(icao);

        var tree = FacilityDecoder.Decode(definition, recorded);
        var encoder = new FacilityEncoder(recorded[0].Protocol) { NextUniqueId = UniqueId(recorded[0]) };
        var encoded = encoder.Encode(definition, tree, requestId);

        Assert.Equal(recorded.Count, encoded.Count);
        for (var i = 0; i < recorded.Count; i++)
        {
            Assert.True(
                recorded[i].Raw.AsSpan().SequenceEqual(encoded[i].Raw),
                $"{icao} packet {i}: expected {Convert.ToHexString(recorded[i].Raw)} got {Convert.ToHexString(encoded[i].Raw)}");
        }
    }

    [Fact]
    public void Ytwb_decodes_to_the_expected_objects()
    {
        var definition = FacilityDefinition.FromNames(FacilityFixture.DefinitionNames(444)).Root!;
        var airport = FacilityDecoder.Decode(definition, FacilityFixture.Reply("YTWB").Packets);

        Assert.Equal("YTWB", airport["ICAO"]);
        Assert.Equal("Toowoomba", airport["NAME64"]);
        Assert.Equal(2, airport.ChildrenOf("RUNWAY").Count);
        Assert.Equal(215, airport.ChildrenOf("TAXI_POINT").Count);
        Assert.Equal(270, airport.ChildrenOf("TAXI_PATH").Count);
        Assert.Equal(36, airport.ChildrenOf("TAXI_PARKING").Count);
        Assert.Equal(4, airport.ChildrenOf("FREQUENCY").Count);
        Assert.Equal(6, airport.ChildrenOf("RUNWAY")[0].Children.Count);
    }

    private static uint UniqueId(Packet p) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(p.Payload[4..]);
}

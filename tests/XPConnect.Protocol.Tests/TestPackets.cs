using XPConnect.Protocol;

namespace XPConnect.Protocol.Tests;

/// <summary>Client packets built the way a SimConnect client library lays them out.</summary>
public static class TestPackets
{
    public const uint SunRise = 6;

    public static Packet Open(string appName = "BeyondATC", uint sendId = 1) =>
        PacketBuilder.Client(0x01, SunRise, sendId)
            .Str(appName, 256).U32(0).U8(0).Str("RS", 3)
            .U32(12).U32(2).U32(282174).U32(999)
            .Build();

    public static Packet AddToDataDefinition(uint defineId, string datum, string units, uint sendId = 2) =>
        PacketBuilder.Client(0x0C, SunRise, sendId)
            .U32(defineId).Str(datum, 256).Str(units, 256).U32(4).F32(0).U32(0xFFFFFFFF)
            .Build();

    public static Packet SubscribeToSystemEvent(uint eventId, string name, uint sendId = 3) =>
        PacketBuilder.Client(0x17, SunRise, sendId).U32(eventId).Str(name, 256).Build();
}

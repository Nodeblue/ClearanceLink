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

    public static Packet AddToDataDefinition(uint defineId, string datum, string units, uint sendId = 2, uint dataType = 4) =>
        PacketBuilder.Client(0x0C, SunRise, sendId)
            .U32(defineId).Str(datum, 256).Str(units, 256).U32(dataType).F32(0).U32(0xFFFFFFFF)
            .Build();

    public static Packet SubscribeToSystemEvent(uint eventId, string name, uint sendId = 3) =>
        PacketBuilder.Client(0x17, SunRise, sendId).U32(eventId).Str(name, 256).Build();

    public static Packet RequestDataOnSimObject(uint requestId, uint defineId, uint period, uint objectId = 0, uint sendId = 4) =>
        PacketBuilder.Client(0x0E, SunRise, sendId)
            .U32(requestId).U32(defineId).U32(objectId).U32(period).U32(0).U32(0).U32(0).U32(0)
            .Build();

    public static Packet MapClientEventToSimEvent(uint eventId, string name, uint sendId = 5) =>
        PacketBuilder.Client(0x04, SunRise, sendId).U32(eventId).Str(name, 256).Build();

    public static Packet TransmitClientEvent(uint objectId, uint eventId, uint data, uint sendId = 6) =>
        PacketBuilder.Client(0x05, SunRise, sendId).U32(objectId).U32(eventId).U32(data).U32(0).U32(16).Build();

    public static Packet RequestFacilityData(uint defineId, uint requestId, string icao, uint sendId = 7) =>
        PacketBuilder.Client(0x46, SunRise, sendId).U32(defineId).U32(requestId).Str(icao, 16).Str("", 4).Build();
}
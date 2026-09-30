using System.Buffers.Binary;

namespace XPConnect.Protocol;

/// <summary>Which way a packet travels. Client = BeyondATC (or any SimConnect client); server = the simulator side.</summary>
public enum Direction
{
    ClientToServer,
    ServerToClient,
}

/// <summary>
/// One raw SimConnect packet. All fields are little-endian.
/// Client-to-server header (16 bytes): size, protocol, 0xF0000000 | functionId, sendId.
/// Server-to-client header (12 bytes): size, protocol, recvId.
/// </summary>
public sealed class Packet
{
    public const int ClientHeaderSize = 16;
    public const int ServerHeaderSize = 12;
    public const uint ClientTypeMask = 0xF0000000;

    public Packet(Direction direction, byte[] raw)
    {
        if (raw.Length < ServerHeaderSize)
        {
            throw new ArgumentException($"Packet too short: {raw.Length} bytes", nameof(raw));
        }

        Direction = direction;
        Raw = raw;
    }

    public Direction Direction { get; }

    public byte[] Raw { get; }

    public uint Size => BinaryPrimitives.ReadUInt32LittleEndian(Raw);

    public uint Protocol => BinaryPrimitives.ReadUInt32LittleEndian(Raw.AsSpan(4));

    /// <summary>Raw type field: 0xF0000000 | functionId for client packets, recvId for server packets.</summary>
    public uint TypeField => BinaryPrimitives.ReadUInt32LittleEndian(Raw.AsSpan(8));

    /// <summary>Client function id with the 0xF0000000 marker removed. Only meaningful for client packets.</summary>
    public uint FunctionId => TypeField & ~ClientTypeMask;

    /// <summary>Client packet serial number, echoed back by the server in exceptions. Null for server packets.</summary>
    public uint? SendId => Direction == Direction.ClientToServer && Raw.Length >= ClientHeaderSize
        ? BinaryPrimitives.ReadUInt32LittleEndian(Raw.AsSpan(12))
        : null;

    public ReadOnlySpan<byte> Payload => Raw.AsSpan(Direction == Direction.ClientToServer ? ClientHeaderSize : ServerHeaderSize);

    public string Name => Direction == Direction.ClientToServer
        ? FunctionNames.Get(FunctionId)
        : RecvNames.Get(TypeField);
}

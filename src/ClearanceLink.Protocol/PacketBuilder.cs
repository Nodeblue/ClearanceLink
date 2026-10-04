using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace ClearanceLink.Protocol;

/// <summary>Builds SimConnect packets. Used by the fake server (server packets) and by tests (client packets).</summary>
public sealed class PacketBuilder
{
    private readonly ArrayBufferWriter<byte> _body = new();
    private readonly Direction _direction;
    private readonly uint _protocol;
    private readonly uint _type;
    private readonly uint _sendId;

    private PacketBuilder(Direction direction, uint protocol, uint type, uint sendId)
    {
        _direction = direction;
        _protocol = protocol;
        _type = type;
        _sendId = sendId;
    }

    public static PacketBuilder Client(uint functionId, uint protocol, uint sendId) =>
        new(Direction.ClientToServer, protocol, Packet.ClientTypeMask | functionId, sendId);

    public static PacketBuilder Server(RecvId recvId, uint protocol) =>
        new(Direction.ServerToClient, protocol, (uint)recvId, 0);

    public PacketBuilder U32(uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        _body.Write(b);
        return this;
    }

    public PacketBuilder F32(float value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(b, value);
        _body.Write(b);
        return this;
    }

    public PacketBuilder F64(double value)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(b, value);
        _body.Write(b);
        return this;
    }

    public PacketBuilder U8(byte value)
    {
        _body.Write([value]);
        return this;
    }

    /// <summary>Fixed-length NUL-padded string, truncated to leave room for the terminator.</summary>
    public PacketBuilder Str(string value, int length)
    {
        var buffer = new byte[length];
        var bytes = Encoding.Latin1.GetBytes(value);
        Array.Copy(bytes, buffer, Math.Min(bytes.Length, length - 1));
        _body.Write(buffer);
        return this;
    }

    public PacketBuilder Bytes(ReadOnlySpan<byte> value)
    {
        _body.Write(value);
        return this;
    }

    public Packet Build()
    {
        var headerSize = _direction == Direction.ClientToServer ? Packet.ClientHeaderSize : Packet.ServerHeaderSize;
        var raw = new byte[headerSize + _body.WrittenCount];
        BinaryPrimitives.WriteUInt32LittleEndian(raw, (uint)raw.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), _protocol);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(8), _type);
        if (_direction == Direction.ClientToServer)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(12), _sendId);
        }

        _body.WrittenSpan.CopyTo(raw.AsSpan(headerSize));
        return new Packet(_direction, raw);
    }
}

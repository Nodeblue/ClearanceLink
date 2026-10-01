using System.Buffers.Binary;
using System.Text;

namespace XPConnect.Protocol;

/// <summary>Sequential little-endian reader over a packet payload. Reads past the end return defaults instead of throwing,
/// so a decoder never crashes on a packet layout we have guessed wrong; <see cref="Overrun"/> records it instead.</summary>
public ref struct PayloadReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _offset;

    public bool Overrun { get; private set; }

    public int Remaining => Math.Max(0, _data.Length - _offset);

    public uint U32() => Take(4, out var s) ? BinaryPrimitives.ReadUInt32LittleEndian(s) : 0;

    public int I32() => Take(4, out var s) ? BinaryPrimitives.ReadInt32LittleEndian(s) : 0;

    public float F32() => Take(4, out var s) ? BinaryPrimitives.ReadSingleLittleEndian(s) : 0;

    public double F64() => Take(8, out var s) ? BinaryPrimitives.ReadDoubleLittleEndian(s) : 0;

    public byte U8() => Take(1, out var s) ? s[0] : (byte)0;

    /// <summary>Fixed-length, NUL-padded ANSI string.</summary>
    public string Str(int length)
    {
        if (!Take(length, out var s))
        {
            return string.Empty;
        }

        var end = s.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? s : s[..end]);
    }

    public byte[] Bytes(int length) => Take(length, out var s) ? s.ToArray() : [];

    public byte[] Rest() => Bytes(Remaining);

    private bool Take(int length, out ReadOnlySpan<byte> slice)
    {
        if (_offset + length > _data.Length)
        {
            Overrun = true;
            slice = default;
            return false;
        }

        slice = _data.Slice(_offset, length);
        _offset += length;
        return true;
    }
}

using System.Buffers.Binary;

namespace ClearanceLink.Protocol;

/// <summary>Reads and writes whole SimConnect packets on a byte stream (named pipe or TCP).</summary>
public static class PacketFramer
{
    /// <summary>Upper bound on a single packet; real facility replies are well under this.</summary>
    public const int MaxPacketSize = 16 * 1024 * 1024;

    /// <summary>Reads one packet, or returns null when the stream ends cleanly between packets.</summary>
    public static async Task<Packet?> ReadAsync(Stream stream, Direction direction, CancellationToken ct = default)
    {
        var sizeBytes = new byte[4];
        if (!await ReadExactlyOrEndAsync(stream, sizeBytes, ct).ConfigureAwait(false))
        {
            return null;
        }

        var size = BinaryPrimitives.ReadInt32LittleEndian(sizeBytes);
        if (size < Packet.ServerHeaderSize || size > MaxPacketSize)
        {
            throw new InvalidDataException($"Invalid SimConnect packet size {size}");
        }

        var raw = new byte[size];
        sizeBytes.CopyTo(raw, 0);
        await stream.ReadExactlyAsync(raw.AsMemory(4), ct).ConfigureAwait(false);
        return new Packet(direction, raw);
    }

    /// <summary>Writes a packet in a single write so message-mode pipes see one message per packet.</summary>
    public static async Task WriteAsync(Stream stream, Packet packet, CancellationToken ct = default)
    {
        await stream.WriteAsync(packet.Raw, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<bool> ReadExactlyOrEndAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (read == 0)
                {
                    return false;
                }

                throw new EndOfStreamException("Stream ended inside a packet header");
            }

            read += n;
        }

        return true;
    }
}

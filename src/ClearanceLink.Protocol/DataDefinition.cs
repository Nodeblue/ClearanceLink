using System.Buffers.Binary;
using System.Text;

namespace ClearanceLink.Protocol;

/// <summary>One entry of a client data definition, as sent in AddToDataDefinition.</summary>
public sealed record Datum(string Name, string Units, uint DataType);

/// <summary>
/// The list of SimVars a client registered under one define id, and the packed block the server sends back for it.
/// Entries are packed in the order they were added, with no padding (checked against the recorded MSFS 2024 replies
/// for the ATC app's defines 300 and 17, see docs/protocol/simconnect.md).
/// </summary>
public sealed class DataDefinition
{
    private readonly List<Datum> _datums = [];

    public IReadOnlyList<Datum> Datums => _datums;

    public void Add(Datum datum) => _datums.Add(datum);

    /// <summary>Byte size of a SIMCONNECT_DATATYPE value, or 0 for types we cannot pack (unknown or variable length).</summary>
    public static int SizeOf(uint dataType) => dataType switch
    {
        1 => 4,    // INT32
        2 => 8,    // INT64
        3 => 4,    // FLOAT32
        4 => 8,    // FLOAT64
        5 => 8,    // STRING8
        6 => 32,   // STRING32
        7 => 64,   // STRING64
        8 => 128,  // STRING128
        9 => 256,  // STRING256
        10 => 260, // STRING260
        12 => 56,  // INITPOSITION
        13 => 68,  // MARKERSTATE
        14 => 48,  // WAYPOINT
        15 => 24,  // LATLONALT
        16 => 24,  // XYZ
        _ => 0,
    };

    /// <summary>
    /// Packs the block. <paramref name="lookup"/> returns a number or string for a SimVar name, or null when the
    /// value is unknown; unknown values and struct types are sent as zeros.
    /// </summary>
    public byte[] Pack(Func<Datum, object?> lookup)
    {
        var block = new byte[_datums.Sum(d => SizeOf(d.DataType))];
        var offset = 0;
        foreach (var datum in _datums)
        {
            var size = SizeOf(datum.DataType);
            var slot = block.AsSpan(offset, size);
            var value = lookup(datum);
            switch (datum.DataType)
            {
                case 1:
                    BinaryPrimitives.WriteInt32LittleEndian(slot, (int)ToDouble(value));
                    break;
                case 2:
                    BinaryPrimitives.WriteInt64LittleEndian(slot, (long)ToDouble(value));
                    break;
                case 3:
                    BinaryPrimitives.WriteSingleLittleEndian(slot, (float)ToDouble(value));
                    break;
                case 4:
                    BinaryPrimitives.WriteDoubleLittleEndian(slot, ToDouble(value));
                    break;
                case >= 5 and <= 10:
                    var bytes = Encoding.Latin1.GetBytes(value as string ?? string.Empty);
                    bytes.AsSpan(0, Math.Min(bytes.Length, size - 1)).CopyTo(slot);
                    break;
            }

            offset += size;
        }

        return block;
    }

    private static double ToDouble(object? value) => value switch
    {
        double d => d,
        bool b => b ? 1 : 0,
        IConvertible c and not string => c.ToDouble(System.Globalization.CultureInfo.InvariantCulture),
        _ => 0,
    };
}

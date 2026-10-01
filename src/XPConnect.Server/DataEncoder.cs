#pragma warning disable CA1720 // Names mirror SIMCONNECT_DATATYPE.
using System.Buffers.Binary;
using System.Text;

namespace XPConnect.Server;

/// <summary>SIMCONNECT_DATATYPE values used in AddToDataDefinition.</summary>
public enum SimConnectDataType : uint
{
    Invalid = 0,
    Int32 = 1,
    Int64 = 2,
    Float32 = 3,
    Float64 = 4,
    String8 = 5,
    String32 = 6,
    String64 = 7,
    String128 = 8,
    String256 = 9,
    String260 = 10,
    StringV = 11,
    InitPosition = 12,
    MarkerState = 13,
    Waypoint = 14,
    LatLonAlt = 15,
    Xyz = 16,
}

/// <summary>Writes one datum in its SimConnect wire representation.</summary>
public static class DataEncoder
{
    /// <summary>Fixed size in bytes, or null for variable-length STRINGV.</summary>
    public static int? SizeOf(SimConnectDataType type) => type switch
    {
        SimConnectDataType.Int32 or SimConnectDataType.Float32 => 4,
        SimConnectDataType.Int64 or SimConnectDataType.Float64 => 8,
        SimConnectDataType.String8 => 8,
        SimConnectDataType.String32 => 32,
        SimConnectDataType.String64 => 64,
        SimConnectDataType.String128 => 128,
        SimConnectDataType.String256 => 256,
        SimConnectDataType.String260 => 260,
        SimConnectDataType.StringV => null,
        SimConnectDataType.InitPosition => 56,
        SimConnectDataType.MarkerState => 68,
        SimConnectDataType.Waypoint => 48,
        SimConnectDataType.LatLonAlt or SimConnectDataType.Xyz => 24,
        _ => 8,
    };

    public static void Write(Stream output, SimConnectDataType type, SimValue value)
    {
        Span<byte> b = stackalloc byte[8];
        switch (type)
        {
            case SimConnectDataType.Int32:
                BinaryPrimitives.WriteInt32LittleEndian(b, (int)Math.Round(value.AsNumber()));
                output.Write(b[..4]);
                break;
            case SimConnectDataType.Int64:
                BinaryPrimitives.WriteInt64LittleEndian(b, (long)Math.Round(value.AsNumber()));
                output.Write(b);
                break;
            case SimConnectDataType.Float32:
                BinaryPrimitives.WriteSingleLittleEndian(b, (float)value.AsNumber());
                output.Write(b[..4]);
                break;
            case SimConnectDataType.Float64:
                BinaryPrimitives.WriteDoubleLittleEndian(b, value.AsNumber());
                output.Write(b);
                break;
            case SimConnectDataType.StringV:
                var text = Encoding.Latin1.GetBytes(value.AsText());
                output.Write(text);
                var padded = (text.Length + 1 + 3) / 4 * 4;
                output.Write(new byte[padded - text.Length]);
                break;
            case >= SimConnectDataType.String8 and <= SimConnectDataType.String260:
                var size = SizeOf(type)!.Value;
                var fixedText = new byte[size];
                var bytes = Encoding.Latin1.GetBytes(value.AsText());
                Array.Copy(bytes, fixedText, Math.Min(bytes.Length, size - 1));
                output.Write(fixedText);
                break;
            default:
                // Struct types (positions, waypoints) are not produced yet; send zeros of the right size.
                output.Write(new byte[SizeOf(type) ?? 8]);
                break;
        }
    }

    /// <summary>True when the client asked for a text type.</summary>
    public static bool IsText(SimConnectDataType type) =>
        type is >= SimConnectDataType.String8 and <= SimConnectDataType.StringV;
}

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace ClearanceLink.Protocol.Facilities;

/// <summary>
/// Turns a <see cref="FacilityObject"/> tree into the FacilityData packets MSFS 2024 sends for a definition:
/// one packet per object, depth-first in definition order, with a unique id that counts up per packet across the
/// whole connection (docs/protocol/facility-data.md).
/// </summary>
public sealed class FacilityEncoder(uint protocol)
{
    private const uint NotAListItem = 0xFFFFFFFF;

    /// <summary>Next uniqueRequestId; MSFS keeps counting across requests on a connection.</summary>
    public uint NextUniqueId { get; set; }

    public List<Packet> Encode(FacilityDefinitionNode definition, FacilityObject root, uint userRequestId)
    {
        var packets = new List<Packet>();
        Emit(definition, root, parentId: 0, isListItem: false, index: NotAListItem, count: NotAListItem, userRequestId, packets);
        return packets;
    }

    public Packet End(uint userRequestId) => PacketBuilder.Server(RecvId.FacilityDataEnd, protocol).U32(userRequestId).Build();

    private void Emit(
        FacilityDefinitionNode definition,
        FacilityObject obj,
        uint parentId,
        bool isListItem,
        uint index,
        uint count,
        uint userRequestId,
        List<Packet> packets)
    {
        var id = NextUniqueId++;
        packets.Add(PacketBuilder.Server(RecvId.FacilityData, protocol)
            .U32(userRequestId).U32(id).U32(parentId).U32(FacilitySchema.TypeCode(definition.Name))
            .U32(isListItem ? 1u : 0u).U32(index).U32(count)
            .Bytes(Pack(definition, obj))
            .Build());

        foreach (var child in definition.Children)
        {
            var items = obj.ChildrenOf(child.Name);
            if (FacilitySchema.IsSingle(child.Name))
            {
                // Single sub-objects are always sent, zero-filled when the source has none.
                Emit(child, items.Count > 0 ? items[0] : new FacilityObject(child.Name), id, false, NotAListItem, NotAListItem, userRequestId, packets);
                continue;
            }

            for (var i = 0; i < items.Count; i++)
            {
                Emit(child, items[i], id, true, (uint)i, (uint)items.Count, userRequestId, packets);
            }
        }
    }

    private static byte[] Pack(FacilityDefinitionNode definition, FacilityObject obj)
    {
        var block = new byte[definition.DataSize];
        var offset = 0;
        foreach (var field in definition.Fields)
        {
            var slot = block.AsSpan(offset, field.Size);
            var value = obj[field.Name];
            switch (field.Type)
            {
                case FacilityFieldType.Int32:
                    BinaryPrimitives.WriteInt32LittleEndian(slot, (int)Math.Round(Number(value)));
                    break;
                case FacilityFieldType.UInt32:
                    BinaryPrimitives.WriteUInt32LittleEndian(slot, (uint)Math.Round(Number(value)));
                    break;
                case FacilityFieldType.Float32:
                    BinaryPrimitives.WriteSingleLittleEndian(slot, (float)Number(value));
                    break;
                case FacilityFieldType.Float64:
                    BinaryPrimitives.WriteDoubleLittleEndian(slot, Number(value));
                    break;
                case FacilityFieldType.Chars:
                    var bytes = Encoding.Latin1.GetBytes(value as string ?? "");
                    bytes.AsSpan(0, Math.Min(bytes.Length, field.Size - 1)).CopyTo(slot);
                    break;
            }

            offset += field.Size;
        }

        return block;
    }

    private static double Number(object? value) => value switch
    {
        null => 0,
        double d => d,
        float f => f,
        bool b => b ? 1 : 0,
        IConvertible c and not string => c.ToDouble(CultureInfo.InvariantCulture),
        _ => 0,
    };
}

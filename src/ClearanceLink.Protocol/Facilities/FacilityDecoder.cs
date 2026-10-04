using System.Buffers.Binary;
using System.Text;

namespace ClearanceLink.Protocol.Facilities;

/// <summary>
/// Rebuilds the <see cref="FacilityObject"/> tree from recorded FacilityData packets and the definition that
/// requested them. Used to study MSFS's answers and to check the encoder reproduces them byte for byte.
/// </summary>
public static class FacilityDecoder
{
    public static FacilityObject Decode(FacilityDefinitionNode definition, IEnumerable<Packet> facilityData)
    {
        var byId = new Dictionary<uint, (FacilityObject Obj, FacilityDefinitionNode Def)>();
        FacilityObject? root = null;
        foreach (var packet in facilityData)
        {
            var p = packet.Payload;
            var id = BinaryPrimitives.ReadUInt32LittleEndian(p[4..]);
            var parentId = BinaryPrimitives.ReadUInt32LittleEndian(p[8..]);
            var type = BinaryPrimitives.ReadUInt32LittleEndian(p[12..]);
            var data = p[28..];

            FacilityDefinitionNode def;
            FacilityObject? parent = null;
            if (root is null)
            {
                def = definition;
            }
            else
            {
                var owner = byId[parentId];
                parent = owner.Obj;
                def = owner.Def.Children.First(c => FacilitySchema.TypeCode(c.Name) == type &&
                    (!FacilitySchema.IsSingle(c.Name) || owner.Obj.ChildrenOf(c.Name).Count == 0));
            }

            var obj = Unpack(def, data);
            byId[id] = (obj, def);
            if (parent is null)
            {
                root = obj;
            }
            else
            {
                parent.Add(obj);
            }
        }

        return root ?? throw new InvalidDataException("No FacilityData packets");
    }

    private static FacilityObject Unpack(FacilityDefinitionNode def, ReadOnlySpan<byte> data)
    {
        var obj = new FacilityObject(def.Name);
        var offset = 0;
        foreach (var field in def.Fields)
        {
            var slot = data.Slice(offset, field.Size);
            obj[field.Name] = field.Type switch
            {
                FacilityFieldType.Int32 => BinaryPrimitives.ReadInt32LittleEndian(slot),
                FacilityFieldType.UInt32 => BinaryPrimitives.ReadUInt32LittleEndian(slot),
                FacilityFieldType.Float32 => BinaryPrimitives.ReadSingleLittleEndian(slot),
                FacilityFieldType.Float64 => BinaryPrimitives.ReadDoubleLittleEndian(slot),
                _ => Encoding.Latin1.GetString(slot[..Math.Max(0, slot.IndexOf((byte)0) is var z and >= 0 ? z : slot.Length)]),
            };
            offset += field.Size;
        }

        return obj;
    }
}

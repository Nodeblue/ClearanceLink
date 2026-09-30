using System.Buffers.Binary;
using System.Text.Json;
using XPConnect.Protocol;
using XPConnect.Recorder;

namespace XPConnect.Host;

/// <summary>
/// Facility data taken from a pass-through recording, so the fake simulator can answer RequestFacilityData with
/// exactly what MSFS sent for the same airport and facility definition. A stopgap until facility data is built
/// from X-Plane's apt.dat and CIFP; it only knows the airports that were requested during the recording.
/// </summary>
public sealed class FacilityReplay
{
    private readonly Dictionary<(string Icao, uint DefineId), List<byte[]>> _replies = [];

    public int Count => _replies.Count;

    public static FacilityReplay FromRecording(string path)
    {
        var replay = new FacilityReplay();
        var open = new Dictionary<(int Conn, uint RequestId), (string Icao, uint DefineId, List<byte[]> Packets)>();
        foreach (var line in File.ReadLines(path))
        {
            // Skip the bulk of a recording without parsing it.
            if (!line.Contains("Facility", StringComparison.Ordinal))
            {
                continue;
            }

            var entry = JsonSerializer.Deserialize<RecordEntry>(line);
            if (entry is null)
            {
                continue;
            }

            switch (entry.Name)
            {
                case "RequestFacilityData" when entry.Dir == "c2s":
                    open[(entry.Connection, Number(entry, "requestId"))] =
                        (entry.Field("icao") ?? "", Number(entry, "defineId"), []);
                    break;
                case "FacilityData" when open.TryGetValue((entry.Connection, Number(entry, "userRequestId")), out var request):
                    request.Packets.Add(Convert.FromHexString(entry.Hex));
                    break;
                case "FacilityDataEnd" when open.Remove((entry.Connection, Number(entry, "requestId")), out var done):
                    replay._replies[(done.Icao.ToUpperInvariant(), done.DefineId)] = done.Packets;
                    break;
            }
        }

        return replay;
    }

    /// <summary>The recorded FacilityData packets for this airport and definition, readdressed to a new request id.</summary>
    public IReadOnlyList<Packet>? Find(string icao, uint defineId, uint requestId)
    {
        if (!_replies.TryGetValue((icao.ToUpperInvariant(), defineId), out var recorded))
        {
            return null;
        }

        return recorded.ConvertAll(raw =>
        {
            var copy = (byte[])raw.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(Packet.ServerHeaderSize), requestId);
            return new Packet(Direction.ServerToClient, copy);
        });
    }

    private static uint Number(RecordEntry entry, string field) =>
        entry.Fields.TryGetValue(field, out var value) && value.TryGetUInt32(out var n) ? n : uint.MaxValue;
}

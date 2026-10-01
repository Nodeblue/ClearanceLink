using System.Text.Json;
using System.Text.Json.Serialization;
using XPConnect.Protocol;

namespace XPConnect.Recorder;

/// <summary>One line of a recording (.jsonl). Contains both decoded fields (for reading) and the raw hex (for replay).</summary>
public sealed record RecordEntry
{
    [JsonPropertyName("t")]
    public DateTimeOffset Time { get; init; }

    [JsonPropertyName("conn")]
    public int Connection { get; init; }

    /// <summary>"c2s" (client to server, i.e. BATC to sim) or "s2c".</summary>
    [JsonPropertyName("dir")]
    public string Dir { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("type")]
    public uint Type { get; init; }

    [JsonPropertyName("protocol")]
    public uint Protocol { get; init; }

    [JsonPropertyName("sendId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? SendId { get; init; }

    [JsonPropertyName("size")]
    public uint Size { get; init; }

    [JsonPropertyName("fields")]
    public Dictionary<string, JsonElement> Fields { get; init; } = [];

    [JsonPropertyName("hex")]
    public string Hex { get; init; } = "";

    public static RecordEntry FromPacket(Packet packet, int connection, DateTimeOffset time)
    {
        var fields = PacketDecoder.Decode(packet)
            .ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value));
        return new RecordEntry
        {
            Time = time,
            Connection = connection,
            Dir = packet.Direction == Direction.ClientToServer ? "c2s" : "s2c",
            Name = packet.Name,
            Type = packet.Direction == Direction.ClientToServer ? packet.FunctionId : packet.TypeField,
            Protocol = packet.Protocol,
            SendId = packet.SendId,
            Size = packet.Size,
            Fields = fields,
            Hex = Convert.ToHexString(packet.Raw),
        };
    }

    public Packet ToPacket() =>
        new(Dir == "c2s" ? Direction.ClientToServer : Direction.ServerToClient, Convert.FromHexString(Hex));

    public string? Field(string name) =>
        Fields.TryGetValue(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()) : null;
}

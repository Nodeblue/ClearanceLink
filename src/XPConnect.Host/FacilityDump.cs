using System.Text.Json;
using System.Text.Json.Nodes;
using XPConnect.Protocol;
using XPConnect.Protocol.Facilities;
using XPConnect.Recorder;

namespace XPConnect.Host;

/// <summary>Decodes an airport answer from a recording into readable JSON (for comparing MSFS with our generator).</summary>
public static class FacilityDump
{
    public static JsonObject FromRecording(string path, string icao)
    {
        var entries = JsonlRecorder.ReadAll(path).ToList();
        var request = entries.FirstOrDefault(e => e.Name == "RequestFacilityData" && string.Equals(e.Field("icao"), icao, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"No RequestFacilityData for {icao} in {path}");
        var defineId = request.Fields["defineId"].GetUInt32();
        var requestId = request.Fields["requestId"].GetUInt32();
        var definition = FacilityDefinition.FromNames(entries
            .Where(e => e.Connection == request.Connection && e.Name == "AddToFacilityDefinition" && e.Fields["defineId"].GetUInt32() == defineId)
            .Select(e => e.Field("fieldName") ?? ""));
        var packets = entries
            .Where(e => e.Connection == request.Connection && e.Name == "FacilityData" && e.Fields["userRequestId"].GetUInt32() == requestId)
            .Select(e => new Packet(Direction.ServerToClient, Convert.FromHexString(e.Hex)));
        return ToJson(FacilityDecoder.Decode(definition.Root!, packets));
    }

    public static JsonObject ToJson(FacilityObject obj)
    {
        var json = new JsonObject { ["_"] = obj.Name };
        foreach (var (key, value) in obj.Values)
        {
            json[key] = value switch
            {
                int i => JsonValue.Create(i),
                uint u => JsonValue.Create(u),
                float f => JsonValue.Create(f),
                double d => JsonValue.Create(d),
                _ => JsonValue.Create(value.ToString()),
            };
        }

        foreach (var (name, children) in obj.Children)
        {
            json[name] = new JsonArray(children.Select(c => (JsonNode)ToJson(c)).ToArray());
        }

        return json;
    }

    public static string Serialize(JsonObject json) => json.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}

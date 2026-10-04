using System.Text.Json;
using ClearanceLink.Protocol;

namespace ClearanceLink.Protocol.Tests;

/// <summary>Reads recordings/samples/batc-1.8.21-facilities.jsonl without depending on the Recorder project.</summary>
public static class FacilityFixture
{
    private static readonly Lazy<List<JsonElement>> Rows = new(Load);

    public static List<string> DefinitionNames(uint defineId) =>
        Rows.Value
            .Where(r => Name(r) == "AddToFacilityDefinition" && r.GetProperty("fields").GetProperty("defineId").GetUInt32() == defineId)
            .Select(r => r.GetProperty("fields").GetProperty("fieldName").GetString()!)
            .ToList();

    /// <summary>The request id and the recorded FacilityData packets answering the request for this airport.</summary>
    public static (uint RequestId, List<Packet> Packets) Reply(string icao)
    {
        var request = Rows.Value.First(r => Name(r) == "RequestFacilityData" && r.GetProperty("fields").GetProperty("icao").GetString() == icao);
        var requestId = request.GetProperty("fields").GetProperty("requestId").GetUInt32();
        var packets = Rows.Value
            .Where(r => Name(r) == "FacilityData" && r.GetProperty("fields").GetProperty("userRequestId").GetUInt32() == requestId)
            .Select(r => new Packet(Direction.ServerToClient, Convert.FromHexString(r.GetProperty("hex").GetString()!)))
            .ToList();
        return (requestId, packets);
    }

    private static string? Name(JsonElement r) => r.GetProperty("name").GetString();

    private static List<JsonElement> Load()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "recordings", "samples", "batc-1.8.21-facilities.jsonl");
            if (File.Exists(path))
            {
                return File.ReadLines(path).Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList();
            }
        }

        throw new FileNotFoundException("batc-1.8.21-facilities.jsonl not found above the test binaries");
    }
}

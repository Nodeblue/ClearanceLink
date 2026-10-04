using ClearanceLink.Protocol;
using ClearanceLink.Protocol.Facilities;

namespace ClearanceLink.Server;

/// <summary>
/// One RequestFacilityData call: the airport, the client's definition for it (null if the client never defined
/// it), and the connection's encoder, whose unique ids carry on across requests as MSFS's do.
/// </summary>
public sealed record FacilityRequest(string Icao, uint DefineId, uint RequestId, FacilityDefinitionNode? Definition, FacilityEncoder Encoder);

/// <summary>Answers RequestFacilityData: the FacilityData packets for an airport, or null when it has none.</summary>
public interface IFacilitySource
{
    /// <summary>Where the data comes from, for logs ("recording X", "X-Plane scenery").</summary>
    string Description { get; }

    IReadOnlyList<Packet>? Find(FacilityRequest request);
}

/// <summary>Asks each source in turn; the first that knows the airport answers.</summary>
public sealed class FacilitySourceChain(params IFacilitySource[] sources) : IFacilitySource
{
    public string Description => string.Join(", then ", sources.Select(s => s.Description));

    public IReadOnlyList<Packet>? Find(FacilityRequest request) => Answer(request)?.Packets;

    /// <summary>The packets and the description of the source that produced them.</summary>
    public (IReadOnlyList<Packet> Packets, string Source)? Answer(FacilityRequest request)
    {
        foreach (var source in sources)
        {
            if (source.Find(request) is { } packets)
            {
                return (packets, source.Description);
            }
        }

        return null;
    }
}

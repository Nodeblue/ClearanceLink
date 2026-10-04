using ClearanceLink.Airports;
using ClearanceLink.Protocol;
using ClearanceLink.Server;

namespace ClearanceLink.Host;

/// <summary>Answers airport requests from the X-Plane installation's own scenery, nav data and CIFP files.</summary>
public sealed class XPlaneFacilitySource(XPlaneAirports airports, AirportGround? ground = null) : IFacilitySource
{
    public string Description => $"X-Plane at {airports.Install.Root}";

    public IReadOnlyList<Packet>? Find(FacilityRequest request)
    {
        if (request.Definition is null || airports.Find(request.Icao) is not { } airport)
        {
            return null;
        }

        ground?.Add(request.Icao, airport);
        return request.Encoder.Encode(request.Definition, airport, request.RequestId);
    }
}

using XPConnect.Protocol;

namespace XPConnect.Server;

/// <summary>Answers RequestFacilityData: the FacilityData packets for an airport, or null when it has none.</summary>
public interface IFacilitySource
{
    /// <summary>Where the data comes from, for logs ("recording X", later "apt.dat").</summary>
    string Description { get; }

    IReadOnlyList<Packet>? Find(string icao, uint defineId, uint requestId);
}

using ClearanceLink.Protocol.Facilities;
using ClearanceLink.Protocol.Tests;

namespace ClearanceLink.Airports.Tests;

/// <summary>X-Plane apt.dat excerpts in fixtures/ and the MSFS answers for the same airports.</summary>
public static class Fixtures
{
    public static Airport Airport(string icao)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "tests", "ClearanceLink.Airports.Tests", "fixtures", $"{icao}.apt.dat");
            if (File.Exists(path))
            {
                using var reader = File.OpenText(path);
                return AptDatParser.Find(reader, icao) ?? throw new InvalidDataException($"{icao} not in {path}");
            }
        }

        throw new FileNotFoundException($"{icao}.apt.dat not found above the test binaries");
    }

    /// <summary>the ATC app's facility definition (444 full, 333 without taxiways).</summary>
    public static FacilityDefinitionNode Definition(uint defineId) => FacilityDefinition.FromNames(FacilityFixture.DefinitionNames(defineId)).Root!;

    /// <summary>What MSFS 2024 answered the ATC app for this airport, decoded.</summary>
    public static FacilityObject Msfs(string icao, uint defineId = 444)
    {
        var (_, packets) = FacilityFixture.Reply(icao);
        return FacilityDecoder.Decode(Definition(defineId), packets);
    }
}

#pragma warning disable CA1720 // Names follow the SimConnect wire types.
namespace XPConnect.Protocol.Facilities;

/// <summary>Wire type of one facility field.</summary>
public enum FacilityFieldType
{
    Int32,
    UInt32,
    Float32,
    Float64,
    Chars,
}

public readonly record struct FacilityField(string Name, FacilityFieldType Type, int Size);

/// <summary>
/// The facility object types and the byte layout of the fields BATC asks for, as MSFS 2024 sends them.
/// Read off recorded replies (docs/protocol/facility-data.md): each object's data block is its requested fields in
/// definition order, packed with no padding. Type codes are SIMCONNECT_FACILITY_DATA_TYPE.
/// </summary>
public static class FacilitySchema
{
    private static readonly Dictionary<string, uint> TypeCodes = new(StringComparer.Ordinal)
    {
        ["AIRPORT"] = 0, ["RUNWAY"] = 1, ["START"] = 2, ["FREQUENCY"] = 3, ["HELIPAD"] = 4, ["APPROACH"] = 5,
        ["APPROACH_TRANSITION"] = 6, ["APPROACH_LEG"] = 7, ["FINAL_APPROACH_LEG"] = 8, ["MISSED_APPROACH_LEG"] = 9,
        ["DEPARTURE"] = 10, ["ARRIVAL"] = 11, ["RUNWAY_TRANSITION"] = 12, ["ENROUTE_TRANSITION"] = 13,
        ["TAXI_POINT"] = 14, ["TAXI_PARKING"] = 15, ["TAXI_PATH"] = 16, ["TAXI_NAME"] = 17, ["JETWAY"] = 18,
        ["VOR"] = 19, ["NDB"] = 20, ["WAYPOINT"] = 21, ["ROUTE"] = 22, ["PAVEMENT"] = 23, ["APPROACH_LIGHTS"] = 24,
        ["VASI"] = 25,
    };

    /// <summary>Runway sub-objects that share the PAVEMENT layout.</summary>
    private static readonly HashSet<string> Pavements = new(StringComparer.Ordinal)
    {
        "PRIMARY_BLASTPAD", "SECONDARY_BLASTPAD", "PRIMARY_THRESHOLD", "SECONDARY_THRESHOLD", "PRIMARY_OVERRUN", "SECONDARY_OVERRUN",
    };

    private static readonly FacilityField[] Leg =
    [
        I("TYPE"), C("FIX_ICAO", 8), C("FIX_REGION", 8), I("FIX_TYPE"), I("APPROACH_ALT_DESC"), F("ALTITUDE1"), F("ALTITUDE2"),
        F("SPEED_LIMIT"), D("FIX_LATITUDE"), D("FIX_LONGITUDE"), F("COURSE"), F("THETA"), I("IS_IAF"), I("IS_IF"), I("IS_FAF"),
        I("IS_MAP"), I("APPROACH_SPEED_DESC"),
    ];

    private static readonly FacilityField[] Procedure = [C("NAME", 8), I("N_ENROUTE_TRANSITIONS"), I("N_RUNWAY_TRANSITIONS"), I("N_APPROACH_LEGS")];

    private static readonly Dictionary<string, Dictionary<string, FacilityField>> Fields = new(StringComparer.Ordinal)
    {
        ["AIRPORT"] = Map(
            D("LATITUDE"), D("LONGITUDE"), D("ALTITUDE"), I("N_RUNWAYS"), I("N_STARTS"), I("N_FREQUENCIES"), I("N_HELIPADS"),
            I("N_APPROACHES"), I("N_DEPARTURES"), I("N_ARRIVALS"), I("N_TAXI_POINTS"), I("N_TAXI_PARKINGS"), I("N_TAXI_PATHS"),
            I("N_TAXI_NAMES"), I("N_JETWAYS"), C("NAME", 32), C("NAME64", 64), C("ICAO", 8), C("REGION", 8), F("MAGVAR"),
            F("TOWER_LATITUDE"), F("TOWER_LONGITUDE"), F("TOWER_ALTITUDE"), F("TRANSITION_ALTITUDE"), F("TRANSITION_LEVEL")),
        ["RUNWAY"] = Map(
            D("LATITUDE"), D("LONGITUDE"), D("ALTITUDE"), F("HEADING"), F("LENGTH"), F("WIDTH"), F("PATTERN_ALTITUDE"),
            F("SLOPE"), F("TRUE_SLOPE"), I("SURFACE"), I("PRIMARY_NUMBER"), I("PRIMARY_DESIGNATOR"), I("SECONDARY_NUMBER"),
            I("SECONDARY_DESIGNATOR"), C("PRIMARY_ILS_ICAO", 8), C("PRIMARY_ILS_REGION", 8), I("PRIMARY_ILS_TYPE"),
            C("SECONDARY_ILS_ICAO", 8), C("SECONDARY_ILS_REGION", 8), I("SECONDARY_ILS_TYPE")),
        ["PAVEMENT"] = Map(F("LENGTH"), F("WIDTH"), I("ENABLE")),
        ["TAXI_PARKING"] = Map(
            I("TYPE"), I("TAXI_POINT_TYPE"), I("NAME"), I("SUFFIX"), U("NUMBER"), I("ORIENTATION"), F("HEADING"), F("RADIUS"),
            F("BIAS_X"), F("BIAS_Z"), I("N_AIRLINES")),
        ["AIRLINE"] = Map(C("NAME", 4)),
        ["TAXI_NAME"] = Map(C("NAME", 32)),
        ["TAXI_PATH"] = Map(
            I("TYPE"), F("WIDTH"), F("LEFT_HALF_WIDTH"), F("RIGHT_HALF_WIDTH"), I("WEIGHT"), I("RUNWAY_NUMBER"),
            I("RUNWAY_DESIGNATOR"), I("LEFT_EDGE"), I("LEFT_EDGE_LIGHTED"), I("RIGHT_EDGE"), I("RIGHT_EDGE_LIGHTED"),
            I("CENTER_LINE"), I("CENTER_LINE_LIGHTED"), I("START"), I("END"), U("NAME_INDEX")),
        ["FREQUENCY"] = Map(I("TYPE"), I("FREQUENCY"), C("NAME", 64)),
        ["TAXI_POINT"] = Map(I("TYPE"), I("ORIENTATION"), F("BIAS_X"), F("BIAS_Z")),
        ["START"] = Map(D("LATITUDE"), D("LONGITUDE"), D("ALTITUDE"), F("HEADING"), I("NUMBER"), I("DESIGNATOR"), I("TYPE")),
        ["APPROACH"] = Map(
            I("TYPE"), I("SUFFIX"), I("RUNWAY_NUMBER"), I("RUNWAY_DESIGNATOR"), C("FAF_ICAO", 8), C("FAF_REGION", 8),
            F("FAF_HEADING"), F("FAF_ALTITUDE"), I("FAF_TYPE"), F("MISSED_ALTITUDE"), I("HAS_LNAV"), I("HAS_LNAVVNAV"),
            I("HAS_LP"), I("HAS_LPV"), I("IS_RNPAR"), I("IS_RNPAR_MISSED"), I("N_TRANSITIONS"), I("N_FINAL_APPROACH_LEGS"),
            I("N_MISSED_APPROACH_LEGS")),
        ["APPROACH_TRANSITION"] = Map(
            I("TYPE"), C("IAF_ICAO", 8), C("IAF_REGION", 8), I("IAF_TYPE"), F("IAF_ALTITUDE"), C("DME_ARC_ICAO", 8),
            C("DME_ARC_REGION", 8), I("DME_ARC_TYPE"), I("DME_ARC_RADIAL"), F("DME_ARC_DISTANCE"), C("NAME", 8), I("N_APPROACH_LEGS")),
        ["APPROACH_LEG"] = Map(Leg),
        ["DEPARTURE"] = Map(Procedure),
        ["ARRIVAL"] = Map(Procedure),
        ["RUNWAY_TRANSITION"] = Map(I("RUNWAY_NUMBER"), I("RUNWAY_DESIGNATOR"), I("N_APPROACH_LEGS")),
        ["ENROUTE_TRANSITION"] = Map(C("NAME", 8), I("N_APPROACH_LEGS")),
    };

    /// <summary>The object's layout name: pavement blocks share PAVEMENT, the three leg kinds share APPROACH_LEG.</summary>
    public static string LayoutOf(string objectName) =>
        Pavements.Contains(objectName) ? "PAVEMENT"
        : objectName is "FINAL_APPROACH_LEG" or "MISSED_APPROACH_LEG" ? "APPROACH_LEG"
        : objectName;

    /// <summary>SIMCONNECT_FACILITY_DATA_TYPE sent in the FacilityData header for this object.</summary>
    public static uint TypeCode(string objectName) =>
        Pavements.Contains(objectName) ? TypeCodes["PAVEMENT"]
        : TypeCodes.TryGetValue(objectName, out var code) ? code
        : throw new KeyNotFoundException($"Unknown facility object {objectName}");

    /// <summary>Single sub-objects (always sent once) rather than lists: the runway pavement blocks.</summary>
    public static bool IsSingle(string objectName) => Pavements.Contains(objectName);

    public static bool TryGetField(string objectName, string fieldName, out FacilityField field)
    {
        field = default;
        return Fields.TryGetValue(LayoutOf(objectName), out var fields) && fields.TryGetValue(fieldName, out field);
    }

    private static Dictionary<string, FacilityField> Map(params FacilityField[] fields) =>
        fields.ToDictionary(f => f.Name, StringComparer.Ordinal);

    private static FacilityField I(string name) => new(name, FacilityFieldType.Int32, 4);

    private static FacilityField U(string name) => new(name, FacilityFieldType.UInt32, 4);

    private static FacilityField F(string name) => new(name, FacilityFieldType.Float32, 4);

    private static FacilityField D(string name) => new(name, FacilityFieldType.Float64, 8);

    private static FacilityField C(string name, int size) => new(name, FacilityFieldType.Chars, size);
}

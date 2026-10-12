namespace ClearanceLink.Host.Status;

/// <summary>
/// Which SimConnect connections make up the ATC app, and what each is for. Any app can be bridged: a connection is
/// known only by the name its Open carries, so an app ClearanceLink has never seen counts as connected as soon as it
/// opens one. Some names were recorded from one ATC app (docs/research/atc-app-call-inventory.md); that app opens more
/// than one connection and is only ready once two of them are open, which the stand-in waits for (ADR-0010). The
/// names are exact, as the app sends them.
/// </summary>
public static class KnownClients
{
    /// <summary>The helper program's connection (the recorded app up to 1.8.21); it asks for airport data.</summary>
    public const string HelperAirports = "BeyondATC";

    /// <summary>The recorded app from 1.8.22, which has no helper: airport data comes on this connection.</summary>
    public const string AirportData = "BATC_Facilities";

    /// <summary>The recorded app's 1.8.22 third connection: it reads the user aircraft on every "Frame" event to time traffic.</summary>
    public const string TrafficTiming = "BATC_TrafficSync";

    /// <summary>The recorded app's main connection; it reads the aircraft and tunes the radios.</summary>
    public const string AircraftAndRadios = "BATCDLLSimConnection";

    /// <summary>True for a connection that asks for airport data.</summary>
    public static bool IsAirports(string name) => name is HelperAirports or AirportData;

    /// <summary>True for a connection whose name was recorded.</summary>
    public static bool IsKnown(string name) => IsAirports(name) || name is AircraftAndRadios or TrafficTiming;

    /// <summary>MSFS's own service, which connects to whatever holds MSFS's default connection; never the ATC app.</summary>
    public const string MsfsBridgeService = "FS2024BridgeService";

    /// <summary>The number of the ATC app's connections that have opened (a connection names itself in its Open).</summary>
    public static int Open(IEnumerable<string> names) => names.Count(IsApp);

    /// <summary>
    /// True when the ATC app is connected: for the recorded app, its main connection and its airport-data connection;
    /// for any other app, any connection.
    /// </summary>
    public static bool AppConnected(IEnumerable<string> names)
    {
        var list = names.Where(IsApp).ToList();
        return list.Any(IsKnown)
            ? list.Contains(AircraftAndRadios) && list.Any(IsAirports)
            : list.Count > 0;
    }

    private static bool IsApp(string name) => name is not ("" or MsfsBridgeService);

    /// <summary>What a connection is for, on the status page.</summary>
    public static string Describe(string name) => name switch
    {
        HelperAirports => "ATC app helper: airport data",
        AirportData => "ATC app: airport data",
        TrafficTiming => "ATC app: traffic timing",
        AircraftAndRadios => "ATC app: aircraft and radios",
        "" => "Connecting",
        MsfsBridgeService => "MSFS service, not used",
        _ => $"SimConnect app ({name})",
    };
}

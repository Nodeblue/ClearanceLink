using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClearanceLink.Server;

namespace ClearanceLink.Host.Status;

/// <summary>
/// What the status page shows: whether X-Plane and BeyondATC are connected, which airports were sent, and a
/// one-line overall state. Built fresh on every request from the live server objects.
/// </summary>
public sealed class StatusBoard(
    Func<IReadOnlyList<SimConnectSession>> sessions,
    Func<XPlaneStatus> xplane,
    string listen,
    TimeProvider? time = null)
{
    /// <summary>The connection BATC's helper (simconnect_ws) opens up to BeyondATC 1.8.21; it asks for airport data.</summary>
    public const string HelperClient = "BeyondATC";

    /// <summary>From BeyondATC 1.8.22 there is no helper: BeyondATC.exe asks for airport data on this connection.</summary>
    public const string FacilitiesClient = "BATC_Facilities";

    /// <summary>BeyondATC 1.8.22's third connection: it reads the user aircraft on every "Frame" event to time traffic.</summary>
    public const string TrafficSyncClient = "BATC_TrafficSync";

    /// <summary>The connection BeyondATC.exe opens; it reads the aircraft and tunes the radios.</summary>
    public const string AppClient = "BATCDLLSimConnection";

    /// <summary>True for the connection that asks for airport data, whichever BeyondATC version opened it.</summary>
    public static bool IsAirportClient(string name) =>
        name.Equals(HelperClient, StringComparison.OrdinalIgnoreCase) || name.Equals(FacilitiesClient, StringComparison.OrdinalIgnoreCase);

    /// <summary>True for any connection BeyondATC opens (as opposed to another MSFS add-on).</summary>
    public static bool IsBatcClient(string name) =>
        IsAirportClient(name)
        || name.Equals(AppClient, StringComparison.OrdinalIgnoreCase)
        || name.Equals(TrafficSyncClient, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the app connection is open.</summary>
    public static bool HasApp(IEnumerable<string> names) => names.Any(n => n.Equals(AppClient, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when BeyondATC is fully connected: its app connection and its airport-data connection.</summary>
    public static bool BatcConnected(IEnumerable<string> names)
    {
        var list = names as IReadOnlyCollection<string> ?? names.ToList();
        return HasApp(list) && list.Any(IsAirportClient);
    }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly DateTimeOffset _started = (time ?? TimeProvider.System).GetUtcNow();

    public string? Stub { get; set; }

    public string? Recording { get; set; }

    /// <summary>The FPLN page's SimBrief uplink; null until serve starts.</summary>
    public ClearanceLink.Host.Support.FlightPlans? FlightPlans { get; set; }

    /// <summary>The PUSH page's pushback; null until serve starts.</summary>
    public ClearanceLink.Host.Pushback.Pushbacks? Pushbacks { get; set; }

    /// <summary>True while the real MSFS runs (it holds the default connection).</summary>
    public Func<bool>? MsfsRunning { get; set; }

    /// <summary>The X-Plane traffic plugin's last report; null when not connected or not started.</summary>
    public Func<ClearanceLink.Host.Traffic.PluginStatus?>? Plugin { get; set; }

    /// <summary>BATC's AI objects, when traffic is on.</summary>
    public Func<IReadOnlyList<TrafficObject>>? Traffic { get; set; }

    public StatusSnapshot Snapshot()
    {
        var now = _time.GetUtcNow();
        var live = sessions();
        var clients = live
            .OrderBy(s => s.ConnectionId)
            .Select(s => new ClientStatus(
                s.ConnectionId,
                s.ClientName,
                Describe(s.ClientName),
                s.Stats.ConnectedAt,
                s.Stats.PacketsReceived,
                s.Stats.DataPacketsSent,
                (now - s.Stats.LastPacketAt).TotalSeconds))
            .ToList();
        var airports = live
            .SelectMany(s => s.Stats.Airports)
            .OrderByDescending(a => a.At)
            .Take(SessionStats.AirportHistory)
            .Select(a => new AirportStatus(a.Icao, a.Source, a.Packets, Math.Round(a.Milliseconds), a.At))
            .ToList();
        var xp = xplane();
        var msfs = MsfsRunning?.Invoke() ?? false;
        return new StatusSnapshot(
            Overall(xp, live, msfs),
            Version,
            _started,
            now,
            listen,
            xp,
            clients,
            airports,
            Stub,
            Recording,
            msfs,
            Plugin?.Invoke() is { } plugin ? new PluginSummary(plugin.Version, plugin.Models, plugin.Aircraft, plugin.CslFolders) : null,
            Traffic?.Invoke().Select(o => new TrafficStatus(
                o.ObjectId,
                o.TailNumber,
                o.Title,
                o.Kind == TrafficKind.Aircraft ? "aircraft" : "object",
                Math.Round(o.Latitude, 5),
                Math.Round(o.Longitude, 5),
                Math.Round(o.AltitudeFeet),
                Math.Round(o.HeadingTrue),
                Math.Round(o.GroundSpeedKnots),
                o.OnGround,
                Math.Round((now - o.LastUpdate).TotalSeconds, 1))).ToList());
    }

    public string ToJson() => JsonSerializer.Serialize(Snapshot(), Json);

    public static string Version { get; } =
        typeof(StatusBoard).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "dev";

    /// <summary>The headline: what works, or the first thing the user has to start.</summary>
    public static OverallStatus Overall(XPlaneStatus xplane, IReadOnlyList<SimConnectSession> sessions, bool msfsRunning = false)
    {
        var names = sessions.Select(s => s.ClientName).ToList();
        var batc = names.Any(IsBatcClient);
        if (msfsRunning && !batc)
        {
            return new(StatusLevel.Problem, "Microsoft Flight Simulator is running. Close it so BeyondATC can connect to X-Plane.");
        }

        return (xplane.Connected, batc) switch
        {
            (false, false) => new(StatusLevel.Waiting, "Waiting for X-Plane. Start X-Plane 12 and load a flight."),
            (false, true) => new(StatusLevel.Problem, "BeyondATC is connected but X-Plane is not. Check X-Plane is running with its Web API on."),
            (true, false) => new(StatusLevel.Waiting, "X-Plane connected. Start BeyondATC."),
            (true, true) when !BatcConnected(names) =>
                new(StatusLevel.Waiting, "X-Plane connected. BeyondATC is still connecting."),
            _ => new(StatusLevel.Ok, "Ready. BeyondATC is talking to X-Plane."),
        };
    }

    private static string Describe(string clientName) => clientName switch
    {
        HelperClient => "BeyondATC helper: airport data",
        FacilitiesClient => "BeyondATC: airport data",
        TrafficSyncClient => "BeyondATC: traffic timing",
        AppClient => "BeyondATC: aircraft and radios",
        "" => "Connecting",
        _ => $"Other MSFS add-on ({clientName}), not used",
    };
}

public enum StatusLevel
{
    Ok,
    Waiting,
    Problem,
}

public sealed record OverallStatus(StatusLevel Level, string Message);

public sealed record XPlaneStatus(bool Connected, string? Version, string Address);

public sealed record ClientStatus(int Id, string Name, string Description, DateTimeOffset ConnectedAt, long PacketsReceived, long DataSent, double SecondsSinceLastPacket);

public sealed record AirportStatus(string Icao, string? Source, int Packets, double Milliseconds, DateTimeOffset At);

public sealed record StatusSnapshot(
    OverallStatus Overall,
    string Version,
    DateTimeOffset Started,
    DateTimeOffset Now,
    string Listen,
    XPlaneStatus XPlane,
    IReadOnlyList<ClientStatus> Clients,
    IReadOnlyList<AirportStatus> Airports,
    string? Stub,
    string? Recording,
    bool MsfsRunning = false,
    PluginSummary? Plugin = null,
    IReadOnlyList<TrafficStatus>? Traffic = null);

public sealed record PluginSummary(string Version, int Models, int Aircraft, IReadOnlyList<string> CslFolders);

public sealed record TrafficStatus(uint Id, string Tail, string Model, string Kind, double Latitude, double Longitude, double AltitudeFeet, double Heading, double GroundSpeedKnots, bool OnGround, double SecondsSinceUpdate);

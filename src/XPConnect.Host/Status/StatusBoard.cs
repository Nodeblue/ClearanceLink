using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using XPConnect.Server;

namespace XPConnect.Host.Status;

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
    /// <summary>The connection BATC's helper (simconnect_ws) opens; it asks for airport data.</summary>
    public const string HelperClient = "BeyondATC";

    /// <summary>The connection BeyondATC.exe opens; it reads the aircraft and tunes the radios.</summary>
    public const string AppClient = "BATCDLLSimConnection";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly DateTimeOffset _started = (time ?? TimeProvider.System).GetUtcNow();

    public string? Stub { get; set; }

    public string? Recording { get; set; }

    /// <summary>True while the real MSFS runs (it holds the default connection).</summary>
    public Func<bool>? MsfsRunning { get; set; }

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
        return new StatusSnapshot(
            Overall(xp, live, MsfsRunning?.Invoke() ?? false),
            Version,
            _started,
            now,
            listen,
            xp,
            clients,
            airports,
            Stub,
            Recording);
    }

    public string ToJson() => JsonSerializer.Serialize(Snapshot(), Json);

    public static string Version { get; } =
        typeof(StatusBoard).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "dev";

    /// <summary>The headline: what works, or the first thing the user has to start.</summary>
    public static OverallStatus Overall(XPlaneStatus xplane, IReadOnlyList<SimConnectSession> sessions, bool msfsRunning = false)
    {
        var names = sessions.Select(s => s.ClientName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var batc = names.Contains(HelperClient) || names.Contains(AppClient);
        if (msfsRunning && !batc)
        {
            return new(StatusLevel.Problem, "Microsoft Flight Simulator is running. Close it so BeyondATC can connect to X-Plane.");
        }

        return (xplane.Connected, batc) switch
        {
            (false, false) => new(StatusLevel.Waiting, "Waiting for X-Plane. Start X-Plane 12 and load a flight."),
            (false, true) => new(StatusLevel.Problem, "BeyondATC is connected but X-Plane is not. Check X-Plane is running with its Web API on."),
            (true, false) => new(StatusLevel.Waiting, "X-Plane connected. Start BeyondATC."),
            (true, true) when !names.Contains(AppClient) || !names.Contains(HelperClient) =>
                new(StatusLevel.Waiting, "X-Plane connected. BeyondATC is still connecting."),
            _ => new(StatusLevel.Ok, "Ready. BeyondATC is talking to X-Plane."),
        };
    }

    private static string Describe(string clientName) => clientName switch
    {
        HelperClient => "BeyondATC helper: airport data",
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
    string? Recording);

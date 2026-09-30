using System.Globalization;
using System.Text;

namespace XPConnect.Recorder;

/// <summary>
/// Summarises what a client asked the simulator for: which calls, which SimVars, which events, which facility
/// fields. This is the to-do list for the X-Plane mapping, and the Phase 0 deliverable.
/// </summary>
public sealed class CallInventory
{
    private readonly SortedDictionary<string, int> _calls = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> _replies = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _simVars = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _clientEvents = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _systemEvents = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _systemStates = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _facilityFields = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _facilityRequests = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _clientData = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _aiModels = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _inputEvents = new(StringComparer.Ordinal);
    private readonly List<string> _opens = [];
    private readonly List<string> _exceptions = [];
    private readonly Dictionary<(int Conn, uint SendId), string> _sentCalls = [];
    private DateTimeOffset? _first;
    private DateTimeOffset? _last;

    public int Count { get; private set; }

    /// <summary>High-rate packets the recorder sampled out; they were forwarded but not written.</summary>
    public IReadOnlyDictionary<string, long> SampledOut { get; set; } = new Dictionary<string, long>();

    public static CallInventory FromFile(string path)
    {
        var inventory = new CallInventory();
        foreach (var entry in JsonlRecorder.ReadAll(path))
        {
            inventory.Add(entry);
        }

        return inventory;
    }

    public void Add(RecordEntry e)
    {
        Count++;
        _first ??= e.Time;
        _last = e.Time;

        if (e.Dir == "s2c")
        {
            Increment(_replies, e.Name);
            if (e.Name == "Open")
            {
                _opens.Add($"server: {e.Field("applicationName")} app {e.Field("appVersionMajor")}.{e.Field("appVersionMinor")}.{e.Field("appBuildMajor")}.{e.Field("appBuildMinor")}, SimConnect {e.Field("simConnectVersionMajor")}.{e.Field("simConnectVersionMinor")}.{e.Field("simConnectBuildMajor")}.{e.Field("simConnectBuildMinor")} (protocol {e.Protocol})");
            }
            else if (e.Name == "Exception")
            {
                var sendId = uint.TryParse(e.Field("sendId"), out var s) ? s : 0;
                var call = _sentCalls.GetValueOrDefault((e.Connection, sendId), "?");
                _exceptions.Add($"exception {e.Field("exception")} for {call} (sendId {sendId}, index {e.Field("index")})");
            }

            return;
        }

        Increment(_calls, e.Name);
        if (e.SendId is { } id)
        {
            _sentCalls[(e.Connection, id)] = e.Name;
        }

        switch (e.Name)
        {
            case "Open":
                _opens.Add($"client: '{e.Field("appName")}' alias {e.Field("alias")} {e.Field("versionMajor")}.{e.Field("versionMinor")}.{e.Field("buildMajor")}.{e.Field("buildMinor")} (protocol {e.Protocol})");
                break;
            case "AddToDataDefinition":
                _simVars.Add($"{e.Field("datumName")} | {e.Field("unitsName")} | type {e.Field("dataType")}");
                break;
            case "MapClientEventToSimEvent":
                _clientEvents.Add(e.Field("eventName") ?? "");
                break;
            case "SubscribeToSystemEvent":
                _systemEvents.Add(e.Field("eventName") ?? "");
                break;
            case "RequestSystemState":
                _systemStates.Add(e.Field("state") ?? "");
                break;
            case "AddToFacilityDefinition":
                _facilityFields.Add(e.Field("fieldName") ?? "");
                break;
            case "RequestFacilityData":
            case "RequestFacilityData_EX1":
                _facilityRequests.Add($"{e.Field("icao")} {e.Field("region")}".Trim());
                break;
            case "MapClientDataNameToID":
                _clientData.Add(e.Field("clientDataName") ?? "");
                break;
            case "MapInputEventToClientEvent":
                _inputEvents.Add(e.Field("inputDefinition") ?? "");
                break;
            default:
                if (e.Name.StartsWith("AICreate", StringComparison.Ordinal))
                {
                    _aiModels.Add($"{e.Field("containerTitle")} {e.Field("livery")}".Trim());
                }

                break;
        }
    }

    public string ToMarkdown(string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# {title}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"{Count} packets, {_first:u} to {_last:u}.");
        Section(sb, "Handshake", _opens);
        sb.AppendLine();
        sb.AppendLine("## Calls (client to sim)");
        sb.AppendLine();
        sb.AppendLine("| Call | Count |");
        sb.AppendLine("|---|---|");
        foreach (var (name, count) in _calls)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {count} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Replies (sim to client)");
        sb.AppendLine();
        sb.AppendLine("| Message | Count |");
        sb.AppendLine("|---|---|");
        foreach (var (name, count) in _replies)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {count} |");
        }

        if (SampledOut.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Sampled out (forwarded, not recorded)");
            sb.AppendLine();
            sb.AppendLine("| Call or message | Count |");
            sb.AppendLine("|---|---|");
            foreach (var (name, count) in SampledOut.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {count} |");
            }
        }

        Section(sb, "SimVars (name | units | data type)", _simVars);
        Section(sb, "Client events mapped to sim events", _clientEvents);
        Section(sb, "System events subscribed", _systemEvents);
        Section(sb, "System states requested", _systemStates);
        Section(sb, "Facility definition fields", _facilityFields);
        Section(sb, "Facilities requested", _facilityRequests);
        Section(sb, "Client data areas", _clientData);
        Section(sb, "Input events mapped", _inputEvents);
        Section(sb, "AI models requested", _aiModels);
        Section(sb, "Exceptions returned", _exceptions);
        return sb.ToString();
    }

    private static void Increment(SortedDictionary<string, int> counts, string key) =>
        counts[key] = counts.GetValueOrDefault(key) + 1;

    private static void Section(StringBuilder sb, string heading, IEnumerable<string> items)
    {
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"## {heading}");
        sb.AppendLine();
        var any = false;
        foreach (var item in items)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- `{item}`");
            any = true;
        }

        if (!any)
        {
            sb.AppendLine("_none_");
        }
    }
}

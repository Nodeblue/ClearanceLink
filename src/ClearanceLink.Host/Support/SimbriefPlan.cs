using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Win32;

namespace ClearanceLink.Host.Support;

/// <summary>The user's latest SimBrief flight and where SimBrief keeps its X-Plane 11/12 flight plan.</summary>
public sealed record SimbriefFlight(string RequestId, string Origin, string Destination, Uri FmsFile)
{
    /// <summary>When SimBrief made the plan, when it says.</summary>
    public DateTimeOffset? Generated { get; init; }

    /// <summary>The route (general.route, e.g. DCT SANEG H91 ADMAR Q404 ADMOD Y50 BOREE BORE5A); null when not given.</summary>
    public string? Route { get; init; }

    /// <summary>Initial cruise altitude in feet, when given.</summary>
    public int? CruiseFeet { get; init; }

    public string? OriginRunway { get; init; }

    public string? DestinationRunway { get; init; }

    /// <summary>SID and STAR names; null when the plan has none (SimBrief sends an empty object then).</summary>
    public string? Sid { get; init; }

    public string? Star { get; init; }

    /// <summary>The name the plan is saved under in X-Plane's Output/FMS plans, e.g. YBBNYSSY.fms. The same route overwrites itself.</summary>
    public string FileName => $"{Origin}{Destination}.fms";
}

/// <summary>
/// Fetches the user's latest SimBrief plan (the one BeyondATC downloads from SimBrief itself) and its X-Plane 11/12
/// flight plan (.fms), in which SimBrief has already expanded the airways and added the runways, SID and STAR. Used by
/// the window's FPLN page and `clearancelink simbrief`; nothing is fetched unless the user asks. The SimBrief pilot ID
/// comes from BeyondATC's own setting, or from settings simbrief-id. Any failure (offline, no plan, SimBrief error)
/// gives null and a log line, never an error.
/// </summary>
public static class SimbriefPlan
{
    /// <summary>BeyondATC's Unity PlayerPrefs; its SimBrief pilot ID is a DWORD named simbriefId_h&lt;hash&gt; (Ben's PC, 2026-10-05).</summary>
    public const string BatcPrefsKey = @"Software\Skirmish Mode Games, Inc\BeyondATC";

    public const string BatcPrefsValue = "simbriefId_h";

    /// <summary>X-Plane's flight plan folder, relative to the X-Plane folder.</summary>
    public static readonly string PlansFolder = Path.Combine("Output", "FMS plans");

    /// <summary>The SimBrief pilot ID: settings simbrief-id when set, else BeyondATC's. Null when neither is known.</summary>
    public static string? PilotId(AppSettings settings) => settings.SimbriefId is { Length: > 0 } id ? id : PilotIdFromBatc();

    /// <summary>The pilot ID BeyondATC saved when the user linked SimBrief, or null.</summary>
    public static string? PilotIdFromBatc()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var prefs = Registry.CurrentUser.OpenSubKey(BatcPrefsKey);
            if (prefs is null)
            {
                return null;
            }

            foreach (var name in prefs.GetValueNames())
            {
                if (name.StartsWith(BatcPrefsValue, StringComparison.Ordinal) && prefs.GetValue(name) is int id && id > 0)
                {
                    return id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Not readable: as if not set.
        }

        return null;
    }

    /// <summary>A numeric pilot ID is sent as userid, anything else as a SimBrief username.</summary>
    public static Uri LatestUri(string pilotId) =>
        new($"https://www.simbrief.com/api/xml.fetcher.php?{(pilotId.All(char.IsAsciiDigit) ? "userid" : "username")}={Uri.EscapeDataString(pilotId)}&json=1");

    /// <summary>Reads SimBrief's latest-OFP JSON; null when it holds no plan with an X-Plane 11/12 download.</summary>
    public static SimbriefFlight? FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("fetch", out var fetch) && fetch.TryGetProperty("status", out var status)
            && status.GetString() is { } text && !text.Equals("Success", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // "xpe" is X-Plane 11/12 with SID and STAR; "xpn" leaves them out.
        if (Text(root, "params", "request_id") is not { } request
            || Icao(Text(root, "origin", "icao_code")) is not { } origin
            || Icao(Text(root, "destination", "icao_code")) is not { } destination
            || !root.TryGetProperty("fms_downloads", out var downloads)
            || Text(root, "fms_downloads", "directory") is not { } directory
            || !downloads.TryGetProperty("xpe", out var xpe)
            || !xpe.TryGetProperty("link", out var link) || link.GetString() is not { Length: > 0 } file
            || file.Contains('/', StringComparison.Ordinal) || file.Contains('\\', StringComparison.Ordinal)
            || !Uri.TryCreate(new Uri(directory.EndsWith('/') ? directory : directory + "/"), file, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var generated = long.TryParse(Text(root, "params", "time_generated"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : (DateTimeOffset?)null;
        var cruise = int.TryParse(Text(root, "general", "initial_altitude"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var feet)
            ? feet
            : (int?)null;
        return new SimbriefFlight(request, origin, destination, uri)
        {
            Generated = generated,
            Route = Text(root, "general", "route") is { Length: > 0 } route ? string.Join(' ', route.Split(' ', StringSplitOptions.RemoveEmptyEntries)) : null,
            CruiseFeet = cruise,
            OriginRunway = Ident(Text(root, "origin", "plan_rwy")),
            DestinationRunway = Ident(Text(root, "destination", "plan_rwy")),
            Sid = Ident(Text(root, "general", "sid_ident")),
            Star = Ident(Text(root, "general", "star_ident")),
        };
    }

    /// <summary>True when the text looks like an X-Plane .fms flight plan (first line I or A, then the format version).</summary>
    public static bool LooksLikeFms(string text)
    {
        var lines = text.Split('\n', 3);
        return lines.Length >= 2 && lines[0].Trim() is "I" or "A" && lines[1].Trim().EndsWith("Version", StringComparison.Ordinal);
    }

    /// <summary>Asks SimBrief for the latest plan of <paramref name="pilotId"/>.</summary>
    public static async Task<SimbriefFlight?> FindAsync(string pilotId, HttpMessageHandler? handler = null, CancellationToken cancel = default)
    {
        try
        {
            using var http = Client(handler);
            var json = await http.GetStringAsync(LatestUri(pilotId), cancel).ConfigureAwait(false);
            var flight = FromJson(json);
            if (flight is null)
            {
                PacketConsole.Info("simbrief: no plan with an X-Plane flight plan download for this pilot ID");
            }

            return flight;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            PacketConsole.Info($"simbrief: could not get the latest plan: {ex.Message}");
            return null;
        }
    }

    /// <summary>Downloads the plan's .fms text; null (and a log line) when it fails or is not an X-Plane flight plan.</summary>
    public static async Task<string?> DownloadAsync(SimbriefFlight flight, HttpMessageHandler? handler = null, CancellationToken cancel = default)
    {
        try
        {
            using var http = Client(handler);
            var text = await http.GetStringAsync(flight.FmsFile, cancel).ConfigureAwait(false);
            if (LooksLikeFms(text))
            {
                return text;
            }

            PacketConsole.Info($"simbrief: {flight.FmsFile} is not an X-Plane flight plan");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            PacketConsole.Info($"simbrief: could not download the flight plan: {ex.Message}");
        }

        return null;
    }

    /// <summary>Writes the plan into X-Plane's Output/FMS plans (via a .tmp file); returns the path, or null.</summary>
    public static string? Save(SimbriefFlight flight, string fms, string xplaneRoot)
    {
        try
        {
            var folder = Path.Combine(xplaneRoot, PlansFolder);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, flight.FileName);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, fms);
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PacketConsole.Info($"simbrief: could not save the flight plan: {ex.Message}");
            return null;
        }
    }

    private static HttpClient Client(HttpMessageHandler? handler)
    {
        var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromSeconds(15);
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ClearanceLink", "1"));
        return http;
    }

    private static string? Text(JsonElement root, string section, string field) =>
        root.TryGetProperty(section, out var s) && s.ValueKind == JsonValueKind.Object && s.TryGetProperty(field, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            }
            : null;

    // Every SimBrief value is a string (numbers too); an empty one comes as {} (read on Ben's PC, 2026-10-05).
    private static string? Ident(string? text) =>
        text is { Length: > 0 and <= 12 } && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-') ? text.ToUpperInvariant() : null;

    private static string? Icao(string? code) =>
        code is { Length: >= 3 and <= 4 } && code.All(char.IsAsciiLetterOrDigit) ? code.ToUpperInvariant() : null;
}

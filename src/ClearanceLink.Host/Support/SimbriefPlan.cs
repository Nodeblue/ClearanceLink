using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Win32;

namespace ClearanceLink.Host.Support;

/// <summary>The user's latest SimBrief flight and where SimBrief keeps its X-Plane 11/12 flight plan.</summary>
public sealed record SimbriefFlight(string RequestId, string Origin, string Destination, Uri FmsFile)
{
    /// <summary>The name the plan is saved under in X-Plane's Output/FMS plans, e.g. YBBNYSSY.fms. The same route overwrites itself.</summary>
    public string FileName => $"{Origin}{Destination}.fms";
}

/// <summary>
/// Saves the user's latest SimBrief plan as an X-Plane flight plan (.fms) so it loads in the aircraft's FMS by name,
/// matching the plan BeyondATC downloads from SimBrief itself. SimBrief has already expanded the airways and added the
/// runways, SID and STAR. The SimBrief pilot ID comes from BeyondATC's own setting, or from settings simbrief-id.
/// Any failure (offline, no plan, SimBrief error) gives null and a log line, never an error.
/// </summary>
public static class SimbriefPlan
{
    /// <summary>BeyondATC's Unity PlayerPrefs; its SimBrief pilot ID is a DWORD named simbriefId_h&lt;hash&gt; (Ben's PC, 2026-10-05).</summary>
    public const string BatcPrefsKey = @"Software\Skirmish Mode Games, Inc\BeyondATC";

    public const string BatcPrefsValue = "simbriefId_h";

    /// <summary>How often a running ClearanceLink asks SimBrief for a newer plan.</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(2);

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

        return new SimbriefFlight(request, origin, destination, uri);
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

    /// <summary>Downloads the plan into X-Plane's Output/FMS plans; returns the file written, or null.</summary>
    public static async Task<string?> SaveAsync(SimbriefFlight flight, string xplaneRoot, HttpMessageHandler? handler = null, CancellationToken cancel = default)
    {
        try
        {
            using var http = Client(handler);
            var text = await http.GetStringAsync(flight.FmsFile, cancel).ConfigureAwait(false);
            if (!LooksLikeFms(text))
            {
                PacketConsole.Info($"simbrief: {flight.FmsFile} is not an X-Plane flight plan; not saved");
                return null;
            }

            var folder = Path.Combine(xplaneRoot, PlansFolder);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, flight.FileName);
            var tmp = path + ".tmp";
            await File.WriteAllTextAsync(tmp, text, cancel).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            PacketConsole.Info($"simbrief: could not save the flight plan: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// While ClearanceLink runs: saves the latest plan now and whenever SimBrief has a newer one. <paramref name="saved"/>
    /// gets the saved file's name (e.g. YBBNYSSY.fms) for the window.
    /// </summary>
    public static async Task FollowAsync(Func<AppSettings> settings, Func<string?> xplaneRoot, Action<string> saved, HttpMessageHandler? handler,
        TimeSpan every, CancellationToken cancel)
    {
        string? last = null;
        var told = false;
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                var now = settings();
                if (now.SimbriefPlans && PilotId(now) is { } id && xplaneRoot() is { } root)
                {
                    if (await FindAsync(id, handler, cancel).ConfigureAwait(false) is { } flight && flight.RequestId != last
                        && await SaveAsync(flight, root, handler, cancel).ConfigureAwait(false) is { } path)
                    {
                        last = flight.RequestId;
                        PacketConsole.Info($"simbrief: saved {flight.Origin}-{flight.Destination} as {path}");
                        saved(flight.FileName);
                    }
                }
                else if (!told && now.SimbriefPlans)
                {
                    told = true;
                    PacketConsole.Info("simbrief: no SimBrief pilot ID (link SimBrief in BeyondATC, or: settings simbrief-id <id>) or no X-Plane folder; no flight plan saved");
                }

                await Task.Delay(every, cancel).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Closing.
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

    private static string? Icao(string? code) =>
        code is { Length: >= 3 and <= 4 } && code.All(char.IsAsciiLetterOrDigit) ? code.ToUpperInvariant() : null;
}

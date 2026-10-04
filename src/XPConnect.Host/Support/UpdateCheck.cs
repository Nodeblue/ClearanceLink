using System.Net.Http.Headers;
using System.Text.Json;

namespace XPConnect.Host.Support;

/// <summary>A published release newer than this XPConnect.</summary>
public sealed record UpdateInfo(string Version, string PageUrl);

/// <summary>
/// Asks GitHub for the newest published release and says whether it is newer than this XPConnect. It only looks:
/// nothing is downloaded or installed, the user downloads the new zip from the release page. Pre-releases and drafts
/// are never offered (GitHub's "latest release" leaves them out). Any failure (offline, private repository, rate limit)
/// gives null, never an error.
/// </summary>
public static class UpdateCheck
{
    /// <summary>The GitHub repository releases are published in. GitHub redirects the old name if it is renamed.</summary>
    public const string Repository = "Nodeblue/XPConnect";

    public static readonly Uri LatestRelease = new($"https://api.github.com/repos/{Repository}/releases/latest");

    public static readonly string ReleasesPage = $"https://github.com/{Repository}/releases";

    private static readonly Lazy<Task<UpdateInfo?>> Once = new(() => FindAsync(Cli.Version));

    /// <summary>The newest release, checked once per run in the background. Null while checking, when up to date, or when the check is off.</summary>
    public static UpdateInfo? Available
    {
        get
        {
            if (!AppSettings.Load(AppFolders.Default.Settings).CheckForUpdates)
            {
                return null;
            }

            var task = Once.Value;
            return task.IsCompletedSuccessfully ? task.Result : null;
        }
    }

    /// <summary>Asks GitHub once. Returns the release when it is newer than <paramref name="current"/>.</summary>
    public static async Task<UpdateInfo?> FindAsync(string current, HttpMessageHandler? handler = null, CancellationToken cancel = default)
    {
        try
        {
            using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            http.Timeout = TimeSpan.FromSeconds(10);
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestRelease);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("XPConnect", Parse(current) is null ? "dev" : current));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                PacketConsole.Info($"update: no release information from GitHub ({(int)response.StatusCode})");
                return null;
            }

            var found = FromJson(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false), current);
            PacketConsole.Info(found is null ? $"update: {current} is the newest release" : $"update: {found.Version} available at {found.PageUrl}");
            return found;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            PacketConsole.Info($"update: could not check for a newer release: {ex.Message}");
            return null;
        }
    }

    /// <summary>Reads GitHub's release JSON; returns it when it is a published release newer than <paramref name="current"/>.</summary>
    public static UpdateInfo? FromJson(string json, string current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()
            || root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()
            || !root.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { } tagName)
        {
            return null;
        }

        var page = root.TryGetProperty("html_url", out var url) && url.GetString() is { } u ? u : ReleasesPage;
        return IsNewer(tagName, current) ? new UpdateInfo(tagName.TrimStart('v', 'V'), page) : null;
    }

    /// <summary>
    /// True when release <paramref name="tag"/> (v0.6.0) is newer than <paramref name="current"/> (0.5.0). A test build
    /// (0.6.0-beta.1) is older than its release (0.6.0). A build without a version (dev) is never offered an update.
    /// </summary>
    public static bool IsNewer(string tag, string current)
    {
        if (Parse(tag) is not { } release || Parse(current) is not { } mine)
        {
            return false;
        }

        var byNumber = release.Number.CompareTo(mine.Number);
        return byNumber > 0 || byNumber == 0 && mine.PreRelease && !release.PreRelease;
    }

    private static (Version Number, bool PreRelease)? Parse(string text)
    {
        text = text.Trim().TrimStart('v', 'V').Split('+')[0];
        var dash = text.IndexOf('-', StringComparison.Ordinal);
        var number = dash < 0 ? text : text[..dash];
        if (!Version.TryParse(number, out var v))
        {
            return null;
        }

        // 0.5 and 0.5.0 are the same release.
        v = new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
        return (v, dash >= 0);
    }

    /// <summary>The line the update command prints.</summary>
    public static string Describe(string current, UpdateInfo? found) => found is null
        ? $"XPConnect {current} is up to date (or GitHub could not be reached). Releases: {ReleasesPage}"
        : $"XPConnect {found.Version} is available (you have {current}). Download it from {found.PageUrl}";
}

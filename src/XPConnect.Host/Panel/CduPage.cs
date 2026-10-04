using XPConnect.Host.Support;
using XPConnect.Host.Traffic;

namespace XPConnect.Host.Panel;

public enum PanelPage
{
    Status,
    Setup,
    Support,
    About,
}

/// <summary>Colours as on a Boeing CDU: cyan labels, white data, green good, amber attention, magenta active.</summary>
public enum CduColor
{
    White,
    Green,
    Cyan,
    Magenta,
    Amber,
}

/// <summary>What a line select key (or a click on that side of a row) does.</summary>
public enum CduKey
{
    None,
    GoStatus,
    GoSetup,
    GoSupport,
    GoAbout,
    ChooseBatcFolder,
    ChooseXPlaneFolder,
    ConnectionAutomatic,
    ConnectionAlwaysOn,
    RemoveOldFiles,
    OpenStatusPage,
    StartBatc,
    SessionLogOn,
    SessionLogOff,
    CreateSupportFile,
    ShowLiveLog,
    OpenLogFolder,
    OpenWebsite,
    InstallTrafficAddOn,
    OpenUpdatePage,
}

public sealed record CduCell(string Text, CduColor Color = CduColor.White);

/// <summary>One of the six rows: a small cyan label line over a data line, each with a left and a right half.</summary>
public sealed record CduRow(string LabelLeft, string LabelRight, CduCell Left, CduCell Right, CduKey KeyLeft = CduKey.None, CduKey KeyRight = CduKey.None)
{
    public static CduRow Empty { get; } = new("", "", new(""), new(""));
}

public sealed record CduPage(string Title, IReadOnlyList<CduRow> Rows, CduCell Scratchpad);

/// <summary>What the pages show, gathered once a second by the controller.</summary>
public sealed record PanelState(
    string Version,
    bool Serving,
    bool XPlaneConnected,
    string? XPlaneVersion,
    bool BatcHelper,
    bool BatcApp,
    int OtherAddOns,
    string? LastAirport,
    string? LastAirportSource,
    bool MsfsRunning,
    ConnectionMode Connection,
    bool OldConfigFiles,
    bool SessionLog,
    long SessionLogBytes,
    int KeepDays,
    TimeSpan Uptime,
    string? BatcFolder,
    string? XPlaneFolder,
    PluginInstall.State TrafficAddOn = PluginInstall.State.NotBundled,
    int? TrafficAircraft = null,
    UpdateInfo? Update = null);

/// <summary>Builds the four pages from the state. Pure, so the pages are tested without a window.</summary>
public static class CduPages
{
    public const int Width = 24;
    public const string AppName = "XPCONNECT";
    public const string Website = "https://github.com/Nodeblue/XPConnect";

    public static CduPage Build(PanelPage page, PanelState s, string? flash = null) => page switch
    {
        PanelPage.Setup => Setup(s, flash),
        PanelPage.Support => Support(s, flash),
        PanelPage.About => About(s, flash),
        _ => Status(s, flash),
    };

    private static CduPage Status(PanelState s, string? flash)
    {
        var batc = (s.BatcHelper, s.BatcApp) switch
        {
            (true, true) => new CduCell("CONNECTED", CduColor.Green),
            (false, false) => new CduCell("WAITING"),
            _ => new CduCell("CONNECTING", CduColor.Amber),
        };
        var rows = new[]
        {
            Row("X-PLANE", "BEYONDATC", s.XPlaneConnected ? new("CONNECTED", CduColor.Green) : new("WAITING"), batc),
            Row("X-PLANE VERSION", "OTHER ADD-ONS", new(s.XPlaneVersion ?? "-"), new(s.OtherAddOns == 0 ? "NONE" : $"{s.OtherAddOns} IGNORED")),
            Row("LAST AIRPORT", "FROM", new(s.LastAirport ?? "-"), new(s.LastAirportSource ?? "")),
            Row("CONNECTION", "SESSION LOG", new(s.Connection == ConnectionMode.Automatic ? "AUTOMATIC" : "ALWAYS ON", CduColor.Magenta),
                s.SessionLog ? new("ON", CduColor.Green) : new("OFF", CduColor.Amber), CduKey.GoSetup, CduKey.GoSupport),
            Row("RUNNING FOR", "TRAFFIC", new(Duration(s.Uptime)), Traffic(s), CduKey.None, s.TrafficAircraft is null ? CduKey.GoSetup : CduKey.None),
            Row("", "", new("<SETUP"), new("SUPPORT>"), CduKey.GoSetup, CduKey.GoSupport),
        };
        return new($"{AppName} STATUS", rows, flash is null ? Advice(s) : new(flash));
    }

    /// <summary>The scratchpad on the status page: what works, or the next thing to do.</summary>
    public static CduCell Advice(PanelState s)
    {
        var batc = s.BatcHelper || s.BatcApp;
        return s switch
        {
            { Serving: false } => new("STARTING..."),
            { MsfsRunning: true } when !batc => new("CLOSE MSFS TO CONNECT", CduColor.Amber),
            { XPlaneConnected: false } when batc => new("X-PLANE NOT CONNECTED", CduColor.Amber),
            { XPlaneConnected: false } => new("START X-PLANE 12"),
            _ when !batc => new("START BEYONDATC"),
            _ when !(s.BatcHelper && s.BatcApp) => new("BEYONDATC CONNECTING"),
            _ => new("READY FOR CLEARANCE", CduColor.Green),
        };
    }

    private static CduPage Setup(PanelState s, string? flash)
    {
        var automatic = s.Connection == ConnectionMode.Automatic;
        var rows = new[]
        {
            Folder("BEYONDATC FOLDER", s.BatcFolder, CduKey.ChooseBatcFolder),
            Folder("X-PLANE FOLDER", s.XPlaneFolder, CduKey.ChooseXPlaneFolder),
            Row("CONNECTION", "",
                new(automatic ? "<AUTOMATIC" : " AUTOMATIC", automatic ? CduColor.Magenta : CduColor.White),
                new(automatic ? "ALWAYS ON" : "ALWAYS ON>", automatic ? CduColor.White : CduColor.Magenta),
                CduKey.ConnectionAutomatic, CduKey.ConnectionAlwaysOn),
            Row("START", "WEB STATUS", new(s.BatcFolder is null ? "" : "<BEYONDATC"), new("OPEN>"),
                s.BatcFolder is null ? CduKey.None : CduKey.StartBatc, CduKey.OpenStatusPage),
            s.OldConfigFiles && automatic
                ? Row("OLD BEYONDATC FILES", "", new("FOUND", CduColor.Amber), new("REMOVE>"), CduKey.None, CduKey.RemoveOldFiles)
                : TrafficAddOn(s),
            Row("", "", new("<STATUS"), new("ABOUT>"), CduKey.GoStatus, CduKey.GoAbout),
        };
        var help = automatic ? "CLOSE MSFS BEFORE FLYING" : "BATC SET TO X-PLANE";
        return new($"{AppName} SETUP", rows, new(flash ?? help, flash is null && !automatic ? CduColor.Magenta : CduColor.White));
    }

    private static CduPage Support(PanelState s, string? flash)
    {
        var rows = new[]
        {
            Row("SESSION LOG", "",
                new(s.SessionLog ? "<ON" : " ON", s.SessionLog ? CduColor.Green : CduColor.White),
                new(s.SessionLog ? "OFF" : "OFF>", s.SessionLog ? CduColor.White : CduColor.Amber),
                CduKey.SessionLogOn, CduKey.SessionLogOff),
            Row("THIS SESSION", "KEPT FOR", new(s.SessionLog ? Size(s.SessionLogBytes) : "NOT LOGGED"), new($"{s.KeepDays} DAYS")),
            Row("SEND TO NODEBLUE", "", new("<CREATE SUPPORT FILE"), new(""), CduKey.CreateSupportFile),
            Row("DETAILS", "", new("<SHOW LIVE LOG"), new(""), CduKey.ShowLiveLog),
            Row("LOG FILES", "", new("<OPEN FOLDER"), new(""), CduKey.OpenLogFolder),
            Row("", "", new("<STATUS"), new("SETUP>"), CduKey.GoStatus, CduKey.GoSetup),
        };
        return new($"{AppName} SUPPORT", rows, new(flash ?? ""));
    }

    private static CduPage About(PanelState s, string? flash)
    {
        var rows = new[]
        {
            Row("", "", new("CONNECT BEYONDATC TO"), new("")),
            Row("", "", new("X-PLANE 12"), new("")),
            Row("VERSION", "LICENCE", new($"{s.Version} EXPERIMENTAL"), new("MIT")),
            s.Update is { } update
                ? Row("MADE BY", "NEW VERSION", new("NODEBLUE", CduColor.Cyan), new($"{update.Version}>", CduColor.Magenta), CduKey.None, CduKey.OpenUpdatePage)
                : Row("MADE BY", "", new("NODEBLUE", CduColor.Cyan), new("")),
            Row("NOT AFFILIATED WITH", "", new("BEYONDATC", CduColor.Amber), new("")),
            Row("", "", new("<STATUS"), new("WEBSITE>"), CduKey.GoStatus, CduKey.OpenWebsite),
        };
        return new(AppName, rows, new(flash ?? ""));
    }

    /// <summary>The status page's traffic half: aircraft drawn in X-Plane, or why there are none.</summary>
    private static CduCell Traffic(PanelState s) => s switch
    {
        { TrafficAircraft: { } n } => new($"{n} AIRCRAFT", CduColor.Green),
        { TrafficAddOn: PluginInstall.State.NotInstalled or PluginInstall.State.Outdated } => new("SEE SETUP", CduColor.Amber),
        { TrafficAddOn: PluginInstall.State.Installed } => new("WAITING"),
        _ => new("-"),
    };

    /// <summary>The X-Plane plugin that draws BATC's traffic, shown to users as the traffic add-on.</summary>
    private static CduRow TrafficAddOn(PanelState s) => s.TrafficAddOn switch
    {
        PluginInstall.State.NotInstalled => Row("TRAFFIC ADD-ON", "", new("NOT INSTALLED", CduColor.Amber), new("INSTALL>"), CduKey.None, CduKey.InstallTrafficAddOn),
        PluginInstall.State.Outdated => Row("TRAFFIC ADD-ON", "", new("NEWER VERSION", CduColor.Amber), new("UPDATE>"), CduKey.None, CduKey.InstallTrafficAddOn),
        PluginInstall.State.Installed => Row("TRAFFIC ADD-ON", "", new("INSTALLED", CduColor.Green), new("")),
        _ => CduRow.Empty,
    };

    private static CduRow Folder(string label, string? folder, CduKey key) => folder is null
        ? Row(label, "", new("NOT FOUND", CduColor.Amber), new("CHOOSE>"), key, key)
        : Row(label, "", new(ShortPath(folder, Width - 6)), new("FOUND", CduColor.Green), key, key);

    /// <summary>Upper-cases and fits both halves into the 24-character line, shortening the left half first.</summary>
    private static CduRow Row(string labelLeft, string labelRight, CduCell left, CduCell right, CduKey keyLeft = CduKey.None, CduKey keyRight = CduKey.None)
    {
        var l = left.Text.ToUpperInvariant();
        var r = right.Text.ToUpperInvariant();
        if (l.Length + r.Length + 1 > Width && l.Length > 0 && r.Length > 0)
        {
            // Keep up to 10 characters of the left half; the right half gets the rest.
            r = Fit(r, Width - 1 - Math.Min(l.Length, 10));
            l = Fit(l, Width - 1 - r.Length);
        }

        return new(labelLeft, labelRight, left with { Text = Fit(l, Width) }, right with { Text = Fit(r, Width) }, keyLeft, keyRight);
    }

    private static string Fit(string text, int max) => text.Length <= max ? text : text[..Math.Max(0, max - 1)] + "~";

    /// <summary>C:\Games\Sims\BeyondATC to C:\...\BEYONDATC when it doesn't fit.</summary>
    public static string ShortPath(string path, int max)
    {
        path = path.TrimEnd('\\', '/');
        if (path.Length <= max)
        {
            return path;
        }

        // Split by hand: these are Windows paths, also when the tests run elsewhere.
        var parts = path.Split('\\', '/');
        var shortened = $"{parts[0]}\\...\\{parts[^1]}";
        return shortened.Length <= max ? shortened : "..." + parts[^1][^Math.Min(parts[^1].Length, max - 3)..];
    }

    public static string Duration(TimeSpan t) => t.TotalHours >= 1
        ? $"{(int)t.TotalHours} HR {t.Minutes} MIN"
        : $"{t.Minutes} MIN";

    public static string Size(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{Math.Max(1, bytes / 1024)} KB",
        _ => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / 1024.0 / 1024.0:0.0} MB"),
    };
}

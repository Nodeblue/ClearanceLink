using System.Collections.Immutable;
using XPConnect.Host.Support;
using XPConnect.Host.Traffic;

namespace XPConnect.Host.Panel;

public enum PanelPage
{
    Status,
    Setup,
    Support,
    About,
    Welcome,
    StartFolders,
    StartTraffic,
    StartModels,
    StartXPlane,
    StartDone,
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
    Next,
    Back,
    FinishGettingStarted,
    ShowGettingStarted,
    OpenHelp,
    OpenModelsHelp,
    ChooseFsltlFolder,
    ConvertFsltl,
}

public sealed record CduCell(string Text, CduColor Color = CduColor.White);

/// <summary>One of the six rows: a small cyan label line over a data line, each with a left and a right half.</summary>
public sealed record CduRow(string LabelLeft, string LabelRight, CduCell Left, CduCell Right, CduKey KeyLeft = CduKey.None, CduKey KeyRight = CduKey.None)
{
    public static CduRow Empty { get; } = new("", "", new(""), new(""));
}

/// <summary>A page; <paramref name="Message"/>, when set, is plain text in the empty rows above the first row with content.</summary>
public sealed record CduPage(string Title, IReadOnlyList<CduRow> Rows, CduCell Scratchpad, IReadOnlyList<CduCell>? Message = null);

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
    int CslPackages = 0,
    string? FsltlFolder = null,
    int FsltlLiveries = 0,
    bool FsltlConverting = false);

/// <summary>Builds the four pages from the state. Pure, so the pages are tested without a window.</summary>
public static class CduPages
{
    public const int Width = 24;
    public const string AppName = "XPCONNECT";

    /// <summary>The product name in sentences (getting-started messages).</summary>
    public const string ProductName = "XPConnect";
    public const string Website = "https://github.com/Nodeblue/XPConnect";
    public const string HelpPage = Website + "/blob/main/docs/help.md";
    public const string ModelsHelp = HelpPage + "#aircraft-models-for-traffic";

    /// <summary>The page keys under the screen; the getting-started pages are reached from SUPPORT or on first run.</summary>
    public static readonly ImmutableArray<PanelPage> MainPages = [PanelPage.Status, PanelPage.Setup, PanelPage.Support, PanelPage.About];

    /// <summary>The getting-started pages in order.</summary>
    public static readonly ImmutableArray<PanelPage> StartPages = [PanelPage.Welcome, PanelPage.StartFolders, PanelPage.StartTraffic, PanelPage.StartModels, PanelPage.StartXPlane, PanelPage.StartDone];

    public static CduPage Build(PanelPage page, PanelState s, string? flash = null) => page switch
    {
        PanelPage.Setup => Setup(s, flash),
        PanelPage.Support => Support(s, flash),
        PanelPage.About => About(s, flash),
        PanelPage.Welcome => Welcome(flash),
        PanelPage.StartFolders => StartFolders(s, flash),
        PanelPage.StartTraffic => StartTraffic(s, flash),
        PanelPage.StartModels => StartModels(s, flash),
        PanelPage.StartXPlane => StartXPlane(flash),
        PanelPage.StartDone => StartDone(s, flash),
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
            Row("DETAILS", "LOG FILES", new("<LIVE LOG"), new("OPEN FOLDER>"), CduKey.ShowLiveLog, CduKey.OpenLogFolder),
            Row("WELCOME PAGES", "HELP", new("<SHOW AGAIN"), new("OPEN PAGE>"), CduKey.ShowGettingStarted, CduKey.OpenHelp),
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
            Row("MADE BY", "", new("NODEBLUE", CduColor.Cyan), new("")),
            Row("NOT AFFILIATED WITH", "", new("BEYONDATC", CduColor.Amber), new("")),
            Row("", "", new("<STATUS"), new("WEBSITE>"), CduKey.GoStatus, CduKey.OpenWebsite),
        };
        return new(AppName, rows, new(flash ?? ""));
    }

    private static string StartTitle(PanelPage page) => $"WELCOME {StartPages.IndexOf(page) + 1}/{StartPages.Length}";

    private static CduRow StartNav(CduKey next = CduKey.Next, string nextText = "NEXT>") =>
        Row("", "", new("<BACK"), new(nextText), CduKey.Back, next);

    private static CduCell Say(string text, CduColor color = CduColor.White) => new(text, color);

    // The getting-started pages read as datalink messages: plain sentences in one size at the top of the screen,
    // with line select keys only on the rows below them, where there is something to press.
    private static CduPage Welcome(string? flash)
    {
        var rows = new[]
        {
            CduRow.Empty, CduRow.Empty, CduRow.Empty, CduRow.Empty, CduRow.Empty,
            Row("", "", new("<SKIP"), new("NEXT>"), CduKey.FinishGettingStarted, CduKey.Next),
        };
        CduCell[] message =
        [
            Say($"Welcome to {ProductName}", CduColor.Green),
            Say("Bring BeyondATC to X-Plane 12: clearances, taxi, handoffs and traffic, right in your sim."),
            Say("Free and experimental, with no warranty. Not affiliated with or endorsed by BeyondATC.", CduColor.Cyan),
        ];
        return new(StartTitle(PanelPage.Welcome), rows, new(flash ?? "BY NODEBLUE"), message);
    }

    private static CduPage StartFolders(PanelState s, string? flash)
    {
        var found = s.BatcFolder is not null && s.XPlaneFolder is not null;
        var rows = new[]
        {
            CduRow.Empty, CduRow.Empty,
            Folder("BEYONDATC FOLDER", s.BatcFolder, CduKey.ChooseBatcFolder),
            Folder("X-PLANE FOLDER", s.XPlaneFolder, CduKey.ChooseXPlaneFolder),
            CduRow.Empty,
            StartNav(),
        };
        CduCell[] message = found
            ? [Say("Found BeyondATC and X-Plane.", CduColor.Green), Say("Nothing in these folders is changed.")]
            : [Say("A folder wasn't found.", CduColor.Amber), Say("Press CHOOSE beside it.")];
        return new(StartTitle(PanelPage.StartFolders), rows, new(flash ?? ""), message);
    }

    private static CduPage StartTraffic(PanelState s, string? flash)
    {
        var bundled = s.TrafficAddOn != PluginInstall.State.NotBundled;
        var rows = new[]
        {
            CduRow.Empty, CduRow.Empty, CduRow.Empty, CduRow.Empty,
            bundled ? TrafficAddOn(s) : CduRow.Empty,
            StartNav(),
        };
        CduCell[] message = bundled
            ?
            [
                Say("BeyondATC's traffic in X-Plane", CduColor.Green),
                Say($"BeyondATC flies its own traffic; {ProductName}'s plugin draws it in X-Plane."),
                Say("Optional. Close X-Plane to install."),
            ]
            :
            [
                Say("BeyondATC's traffic in X-Plane", CduColor.Green),
                Say("This copy has no X-Plane plugin, so BeyondATC's traffic won't be drawn. ATC still works."),
            ];
        return new(StartTitle(PanelPage.StartTraffic), rows, new(flash ?? ""), message);
    }

    private static CduPage StartModels(PanelState s, string? flash)
    {
        var fsltl = s switch
        {
            { FsltlConverting: true } => Row("FSLTL", "", new("CONVERTING...", CduColor.Amber), new("")),
            { FsltlLiveries: > 0 } => Row("FSLTL", "", new($"{s.FsltlLiveries} LIVERIES", CduColor.Green), new(s.FsltlFolder is null ? "" : "UPDATE>"),
                CduKey.None, s.FsltlFolder is null ? CduKey.None : CduKey.ConvertFsltl),
            { FsltlFolder: not null } => Row("FSLTL", "", new("FOUND IN MSFS", CduColor.Green), new("CONVERT>"), CduKey.None, CduKey.ConvertFsltl),
            _ => Row("FSLTL", "", new("NOT FOUND"), new("CHOOSE>"), CduKey.ChooseFsltlFolder, CduKey.ChooseFsltlFolder),
        };
        var rows = new[]
        {
            CduRow.Empty, CduRow.Empty, CduRow.Empty,
            Row("BLUEBELL", "", s.CslPackages > 0 ? new($"{s.CslPackages} PACKAGES", CduColor.Green) : new("NOT FOUND"),
                new("HOW TO>"), CduKey.None, CduKey.OpenModelsHelp),
            fsltl,
            StartNav(),
        };
        CduCell[] message =
        [
            Say("Aircraft models", CduColor.Green),
            Say("Either tested library works. FSLTL is converted from your MSFS copy."),
        ];
        var help = s.FsltlFolder is not null && s.FsltlLiveries == 0 && !s.FsltlConverting ? "CLOSE X-PLANE TO CONVERT" : "";
        return new(StartTitle(PanelPage.StartModels), rows, new(flash ?? help), message);
    }

    private static CduPage StartXPlane(string? flash)
    {
        var rows = new[]
        {
            CduRow.Empty, CduRow.Empty, CduRow.Empty, CduRow.Empty,
            Row("", "", new(""), new("MORE HELP>"), CduKey.None, CduKey.OpenHelp),
            StartNav(),
        };
        CduCell[] message =
        [
            Say("Before you fly", CduColor.Green),
            Say("In X-Plane's settings, so only BeyondATC talks and flies traffic:"),
            Say("\u2022 ATC volume to 0 (Sound)\n\u2022 AI aircraft off\n\u2022 Parked aircraft off", CduColor.Cyan),
        ];
        return new(StartTitle(PanelPage.StartXPlane), rows, new(flash ?? ""), message);
    }

    private static CduPage StartDone(PanelState s, string? flash)
    {
        var automatic = s.Connection == ConnectionMode.Automatic;
        var rows = new[]
        {
            CduRow.Empty, CduRow.Empty, CduRow.Empty, CduRow.Empty,
            Row("", "", new(""), new("HELP PAGE>"), CduKey.None, CduKey.OpenHelp),
            StartNav(CduKey.FinishGettingStarted, "FINISH>"),
        };
        CduCell[] message =
        [
            Say("You're all set", CduColor.Green),
            Say($"Every flight:\n1. {(automatic ? "Close MSFS" : "MSFS can stay open")}\n2. Start X-Plane 12\n3. Keep {ProductName} open\n4. Start BeyondATC", CduColor.Cyan),
        ];
        return new(StartTitle(PanelPage.StartDone), rows, new(flash ?? "REOPEN ON SUPPORT PAGE"), message);
    }

    /// <summary>The status page's traffic half: aircraft drawn in X-Plane, or why there are none.</summary>
    private static CduCell Traffic(PanelState s) => s switch
    {
        { TrafficAircraft: { } n } => new($"{n} AIRCRAFT", CduColor.Green),
        { TrafficAddOn: PluginInstall.State.NotInstalled or PluginInstall.State.Outdated } => new("SEE SETUP", CduColor.Amber),
        { TrafficAddOn: PluginInstall.State.Installed } => new("WAITING"),
        _ => new("-"),
    };

    /// <summary>The X-Plane plugin that draws BATC's traffic, shown to users as the X-Plane plugin.</summary>
    private static CduRow TrafficAddOn(PanelState s) => s.TrafficAddOn switch
    {
        PluginInstall.State.NotInstalled => Row("X-PLANE PLUGIN", "", new("NOT INSTALLED", CduColor.Amber), new("INSTALL>"), CduKey.None, CduKey.InstallTrafficAddOn),
        PluginInstall.State.Outdated => Row("X-PLANE PLUGIN", "", new("NEWER VERSION", CduColor.Amber), new("UPDATE>"), CduKey.None, CduKey.InstallTrafficAddOn),
        PluginInstall.State.Installed => Row("X-PLANE PLUGIN", "", new("INSTALLED", CduColor.Green), new("")),
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

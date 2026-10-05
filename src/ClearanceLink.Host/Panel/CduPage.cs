using System.Collections.Immutable;
using ClearanceLink.Fsltl;
using ClearanceLink.Host.Pushback;
using ClearanceLink.Host.Support;
using ClearanceLink.Host.Traffic;

namespace ClearanceLink.Host.Panel;

public enum PanelPage
{
    Status,
    FlightPlan,
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
    OpenUpdatePage,
    GoModels,
    GoFlightPlan,
    RequestPlan,
    LoadPlan,
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
    bool BatcRedirected,
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
    bool FsltlConverting = false,
    UpdateInfo? Update = null,
    int? PluginModels = null,
    string? FsltlMessage = null,
    bool FsltlOutOfDate = false,
    ImportProgress? FsltlProgress = null,
    bool XPlaneRunning = false,
    bool MsfsInstalled = true,
    FlightPlanView? FlightPlan = null,
    bool LoadArmed = false,
    PushbackAddOn Pushback = PushbackAddOn.Unknown);

/// <summary>Builds the four pages from the state. Pure, so the pages are tested without a window.</summary>
public static class CduPages
{
    public const int Width = 24;
    public const string AppName = "CLEARANCELINK";

    /// <summary>The product name in sentences (getting-started messages).</summary>
    public const string ProductName = "ClearanceLink";
    public const string Website = "https://github.com/Nodeblue/ClearanceLink";
    public const string HelpPage = Website + "/blob/main/docs/help.md";
    public const string ModelsHelp = HelpPage + "#aircraft-models-for-traffic";

    /// <summary>The page keys under the screen; the getting-started pages are reached from SUPPORT or on first run.</summary>
    public static readonly ImmutableArray<PanelPage> MainPages = [PanelPage.Status, PanelPage.FlightPlan, PanelPage.Setup, PanelPage.Support, PanelPage.About];

    /// <summary>The name on a page key under the screen.</summary>
    public static string KeyName(PanelPage page) => page == PanelPage.FlightPlan ? "FPLN" : page.ToString().ToUpperInvariant();

    /// <summary>The getting-started pages in order.</summary>
    public static readonly ImmutableArray<PanelPage> StartPages = [PanelPage.Welcome, PanelPage.StartFolders, PanelPage.StartTraffic, PanelPage.StartModels, PanelPage.StartXPlane, PanelPage.StartDone];

    public static CduPage Build(PanelPage page, PanelState s, string? flash = null) => page switch
    {
        PanelPage.FlightPlan => FlightPlan(s, flash),
        PanelPage.Setup => Setup(s, flash),
        PanelPage.Support => Support(s, flash),
        PanelPage.About => About(s, flash),
        PanelPage.Welcome => Welcome(flash),
        PanelPage.StartFolders => StartFolders(s, flash),
        PanelPage.StartTraffic => StartTraffic(s, flash),
        PanelPage.StartModels => StartModels(s, flash),
        PanelPage.StartXPlane => StartXPlane(flash),
        PanelPage.StartDone => StartDone(flash),
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
            Row("ATC AIRPORT", "SCENERY", new(s.LastAirport ?? "-"), new(s.LastAirportSource ?? "")),
            Row("CONNECTION", "SESSION LOG", new(s.Connection == ConnectionMode.Automatic ? "AUTOMATIC" : "ALWAYS ON", CduColor.Magenta),
                s.SessionLog ? new("ON", CduColor.Green) : new("OFF", CduColor.Amber), CduKey.GoSetup, CduKey.GoSupport),
            Row("PUSHBACK", Traffic(s).Text == UpdateModels ? "FSLTL MODELS" : "TRAFFIC", Pushback(s), Traffic(s), CduKey.None, s switch
            {
                { TrafficAircraft: not null, PluginModels: 0 } => CduKey.GoModels,
                { FsltlOutOfDate: true, FsltlConverting: false } => CduKey.GoModels,
                { TrafficAircraft: null } => CduKey.GoSetup,
                _ => CduKey.None,
            }),
            Row("", "", new("<SETUP"), new("FPLN>"), CduKey.GoSetup, CduKey.GoFlightPlan),
        };
        return new($"{AppName} STATUS", rows, flash is null ? Advice(s) : new(flash));
    }

    /// <summary>
    /// The scratchpad on the status page: what works, or the next thing to do. Before a flight, a newer release and
    /// FSLTL models that need converting again take turns with it every few seconds; never while something needs
    /// fixing (amber) or once BeyondATC is talking.
    /// </summary>
    public static CduCell Advice(PanelState s)
    {
        var advice = NextStep(s);
        if (advice.Color != CduColor.White || !s.Serving)
        {
            return advice;
        }

        var turns = new List<CduCell> { advice };
        if (s.Update is { } update)
        {
            turns.Add(new(Fit($"NEW VERSION {update.Version}", Width), CduColor.Magenta));
        }

        if (s.FsltlOutOfDate && !s.FsltlConverting)
        {
            turns.Add(new("FSLTL MODELS OUT OF DATE", CduColor.Amber));
        }

        // A plan received on the FPLN page but not loaded yet: say where it is (the FSLTL notice lesson).
        if (s.FlightPlan is { Plan: not null, SavedAs: null })
        {
            turns.Add(new("FLIGHT PLAN READY: FPLN", CduColor.Green));
        }

        return turns[(int)(s.Uptime.TotalSeconds / UpdateTurnSeconds) % turns.Count];
    }

    /// <summary>How long the next step and each notice stay on the status page's bottom line.</summary>
    public const int UpdateTurnSeconds = 4;

    private static CduCell NextStep(PanelState s)
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

    /// <summary>
    /// The SimBrief uplink, like a jet's FMC route request: REQUEST shows the latest plan without touching X-Plane;
    /// LOAD (pressed twice, as it replaces the FMS's plan) saves it into Output/FMS plans and sends it to X-Plane's FMS.
    /// </summary>
    private static CduPage FlightPlan(PanelState s, string? flash)
    {
        var view = s.FlightPlan ?? new FlightPlanView();
        var p = view.Plan;
        CduCell None() => new("-----");
        var fms = view switch
        {
            { FmsProblem: not null } => new CduCell("NOT TAKEN", CduColor.Amber),
            { SentAt: { } at } => new CduCell($"SENT {Zulu(at)}", CduColor.Green),
            { SavedAs: not null } => new CduCell("FILE SAVED", CduColor.Green),
            _ => None(),
        };
        var rows = new[]
        {
            Row("SIMBRIEF PLAN", "CRUISE", p is null ? None() : new($"{p.Origin}-{p.Destination}", CduColor.Green),
                p?.CruiseFeet is { } feet ? new($"FL{feet / 100:000}") : None()),
            Row("RWY/SID", "STAR/RWY", p is null ? None() : new($"{p.OriginRunway ?? "-"}/{p.Sid ?? "-"}"),
                p is null ? None() : new($"{p.Star ?? "-"}/{p.DestinationRunway ?? "-"}")),
            Row("ROUTE", "", p?.Route is { } route ? new(Fit(route.StartsWith("DCT ", StringComparison.Ordinal) ? route[4..] : route, Width)) : None(), new("")),
            Row("MADE", "FMS", p?.Generated is { } made ? new(Zulu(made)) : None(), fms),
            Row("", "", new(view.Requesting ? " REQUESTING" : "<REQUEST"),
                p is null ? new("") : s.LoadArmed ? new("CONFIRM>", CduColor.Amber) : new("LOAD>"),
                view.Requesting ? CduKey.None : CduKey.RequestPlan, p is null ? CduKey.None : CduKey.LoadPlan),
            Row("", "", new("<STATUS"), new("SETUP>"), CduKey.GoStatus, CduKey.GoSetup),
        };
        var hint = view switch
        {
            _ when s.LoadArmed => new CduCell("REPLACES THE FMS PLAN", CduColor.Amber),
            { FmsProblem: { } why } => new CduCell(Fit($"FMS: {why}".ToUpperInvariant(), Width), CduColor.Amber),
            { Plan: null } => new CduCell("PRESS REQUEST"),
            { SavedAs: null } => new CduCell("CHECK, THEN LOAD"),
            { SentAt: not null } => new CduCell("CHECK RWY/SID/STAR"),
            _ => new CduCell(Fit($"LOAD {Path.GetFileNameWithoutExtension(view.SavedAs)} IN THE FMS", Width)),
        };
        return new($"{AppName} FPLN", rows, flash is null ? hint : new(flash));
    }

    private static string Zulu(DateTimeOffset at) => at.UtcDateTime.ToString("HHmm'Z'", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>SETUP's bottom line while connection automatic has BeyondATC pointed at ClearanceLink.</summary>
    public const string BatcUntilClose = "BATC: X-PLANE TILL EXIT";

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
            s is { TrafficAddOn: PluginInstall.State.Installed, FsltlOutOfDate: true, FsltlConverting: false }
                    ? Row("FSLTL MODELS", "", new("OUT OF DATE", CduColor.Amber), new("UPDATE>"), CduKey.None, CduKey.GoModels)
                    : TrafficAddOn(s),
            Row("", "", new("<STATUS"), new("ABOUT>"), CduKey.GoStatus, CduKey.GoAbout),
        };
        // Where BeyondATC goes right now. Automatic points it at X-Plane only while ClearanceLink is open and MSFS is
        // closed (ADR-0012); closing ClearanceLink sends it back to MSFS.
        var help = s switch
        {
            _ when !automatic => new CduCell("BATC SET TO X-PLANE", CduColor.Magenta),
            { MsfsRunning: true } => new CduCell("CLOSE MSFS BEFORE FLYING"),
            { BatcRedirected: true } => new CduCell(BatcUntilClose, CduColor.Magenta),
            _ => new CduCell(""),
        };
        return new($"{AppName} SETUP", rows, flash is null ? help : new(flash));
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
            CduRow.Empty, CduRow.Empty, CduRow.Empty, CduRow.Empty,
            s.Update is { } update
                ? Row("RUNNING FOR", "NEW VERSION", new(Duration(s.Uptime)), new($"{update.Version}>", CduColor.Magenta), CduKey.None, CduKey.OpenUpdatePage)
                : Row("RUNNING FOR", "", new(Duration(s.Uptime)), new("")),
            Row("", "", new("<STATUS"), new("WEBSITE>"), CduKey.GoStatus, CduKey.OpenWebsite),
        };
        CduCell[] message =
        [
            Say($"{ProductName} {s.Version}", CduColor.Green),
            Say("Connect BeyondATC\nto X-Plane 12."),
            Say("Free and experimental, by Nodeblue. Not affiliated with BeyondATC.", CduColor.Cyan),
        ];
        return new(AppName, rows, new(flash ?? ""), message);
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
            { FsltlConverting: true } => Row("FSLTL", "", new(Progress(s.FsltlProgress), CduColor.Amber), new("")),
            { FsltlLiveries: > 0, FsltlOutOfDate: true, FsltlFolder: not null } =>
                Row("FSLTL", "", new("UPDATE NEEDED", CduColor.Amber), new("UPDATE>"), CduKey.None, CduKey.ConvertFsltl),
            { FsltlLiveries: > 0 } => Row("FSLTL", "", new($"{s.FsltlLiveries} LIVERIES", CduColor.Green), new(s.FsltlFolder is null ? "" : "UPDATE>"),
                CduKey.None, s.FsltlFolder is null ? CduKey.None : CduKey.ConvertFsltl),
            { FsltlFolder: not null } => Row("FSLTL", "", new(s.MsfsInstalled ? "FOUND IN MSFS" : "FOUND", CduColor.Green), new("CONVERT>"), CduKey.None, CduKey.ConvertFsltl),
            // No MSFS on this PC: FSLTL can still be copied from another PC, so the folder can still be chosen.
            { MsfsInstalled: false } => Row("FSLTL", "", new("NEEDS MSFS"), new("CHOOSE>"), CduKey.ChooseFsltlFolder, CduKey.ChooseFsltlFolder),
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
            s.FsltlFolder is null && !s.MsfsInstalled
                ? Say("Either tested library works. FSLTL comes from MSFS, which isn't on this PC; CHOOSE a copied FSLTL folder.")
                : Say("Either tested library works. FSLTL is converted from your MSFS copy."),
        ];
        var toConvert = s.FsltlFolder is not null && (s.FsltlLiveries == 0 || s.FsltlOutOfDate);
        var help = s switch
        {
            // Closing the window would stop the conversion (the window asks first).
            { FsltlConverting: true } => "KEEP THIS WINDOW OPEN",
            { XPlaneRunning: true } when toConvert => s.FsltlLiveries == 0 ? "CLOSE X-PLANE TO CONVERT" : "CLOSE X-PLANE TO UPDATE",
            _ => "",
        };
        // The last conversion's result stays on the page (it ran in the background, so the key's flash is long gone).
        var line = flash ?? s.FsltlMessage ?? help;
        return new(StartTitle(PanelPage.StartModels), rows, new(line, line.StartsWith("CONVERT FAILED", StringComparison.Ordinal) ? CduColor.Amber : CduColor.White), message);
    }

    private static CduPage StartXPlane(string? flash)
    {
        var rows = new[]
        {
            CduRow.Empty, CduRow.Empty, CduRow.Empty, CduRow.Empty, CduRow.Empty,
            StartNav(),
        };
        CduCell[] message =
        [
            Say("In X-Plane's settings", CduColor.Green),
            Say("\u2022 Sound: Radios to 0\n\u2022 Graphics: Draw parked aircraft off\n\u2022 Flight Configuration > AI Aircraft: none", CduColor.Cyan),
            Say("So only BeyondATC talks and flies traffic."),
        ];
        return new(StartTitle(PanelPage.StartXPlane), rows, new(flash ?? ""), message);
    }

    private static CduPage StartDone(string? flash)
    {
        var rows = new[]
        {
            CduRow.Empty, CduRow.Empty, CduRow.Empty, CduRow.Empty,
            Row("", "", new(""), new("HELP PAGE>"), CduKey.None, CduKey.OpenHelp),
            StartNav(CduKey.FinishGettingStarted, "FINISH>"),
        };
        CduCell[] message =
        [
            Say("You're all set", CduColor.Green),
            Say($"Every flight:\n1. Plan it in SimBrief\n2. Start X-Plane 12 at the SimBrief departure\n3. Keep {ProductName} open\n4. Start BeyondATC", CduColor.Cyan),
        ];
        return new(StartTitle(PanelPage.StartDone), rows, new(flash ?? "REOPEN ON SUPPORT PAGE"), message);
    }

    /// <summary>How far a conversion is, for the FSLTL row: "TEXTURES 1200/3112".</summary>
    public static string Progress(ImportProgress? p) => p switch
    {
        { Stage: ImportStage.Models, Total: > 0 } => $"MODELS {p.Done}/{p.Total}",
        { Stage: ImportStage.Textures, Total: > 0 } => $"TEXTURES {p.Done}/{p.Total}",
        { Stage: ImportStage.Saving } => "SAVING...",
        _ => "CONVERTING...",
    };

    /// <summary>The status page's traffic half: aircraft drawn in X-Plane, or why there are none.</summary>
    private const string UpdateModels = "UPDATE>";

    private static CduCell Traffic(PanelState s) => s switch
    {
        // The plugin runs but has no CSL package to draw with: the key opens the aircraft models page.
        { TrafficAircraft: not null, PluginModels: 0 } => new("NO MODELS", CduColor.Amber),
        // FSLTL changed since the models were converted: the key opens the aircraft models page, which has UPDATE.
        // While aircraft are drawn the count stays (the old models still work); the key still opens that page.
        { FsltlOutOfDate: true, FsltlConverting: false, TrafficAircraft: null or 0 } => new(UpdateModels, CduColor.Amber),
        { TrafficAircraft: { } n } => new($"{n} AIRCRAFT", CduColor.Green),
        { TrafficAddOn: PluginInstall.State.NotInstalled or PluginInstall.State.Outdated } => new("SEE SETUP", CduColor.Amber),
        { TrafficAddOn: PluginInstall.State.Installed } => new("WAITING"),
        _ => new("-"),
    };

    /// <summary>
    /// The status page's pushback half: whether Better Pushback is in X-Plane. Without it, BeyondATC's pushback
    /// requests are not passed on, so the tug never comes (not a fault: white, not amber).
    /// </summary>
    private static CduCell Pushback(PanelState s) => (s.XPlaneConnected, s.Pushback) switch
    {
        (true, PushbackAddOn.Installed) => new("BETTER PB", CduColor.Green),
        (true, PushbackAddOn.NotInstalled) => new("NOT FOUND"),
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

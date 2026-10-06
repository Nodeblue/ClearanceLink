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
    Push,
    Setup,
    Support,
    About,
    Welcome,
    StartFolders,
    StartTraffic,
    StartModels,
    StartXPlane,
    StartDone,
    Traffic,
    Fsltl,
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
    GoPush,
    PushTailLeft,
    PushTailRight,
    PushStraight,
    PushStart,
    PushStop,
    PushAsCleared,
    PushAuto,
    PushManual,
    GoTraffic,
    OpenXPlaneSettingsHelp,
    GoFsltl,
}

public sealed record CduCell(string Text, CduColor Color = CduColor.White);

/// <summary>One of the six rows: a small cyan label line over a data line, each with a left and a right half.</summary>
public sealed record CduRow(string LabelLeft, string LabelRight, CduCell Left, CduCell Right, CduKey KeyLeft = CduKey.None, CduKey KeyRight = CduKey.None)
{
    /// <summary>What the row means, in a sentence or two: shown when the mouse rests on the row's "i".</summary>
    public string? Help { get; init; }

    /// <summary>
    /// Where the "i" goes: after the left label, or after the right label when only the right half has one (SUPPORT's
    /// HELP), so it sits beside the words it explains. Rows without a label belong to the labelled row above: none.
    /// </summary>
    public HelpMark Mark => Help is null ? HelpMark.None
        : LabelLeft.Length > 0 ? HelpMark.Left
        : LabelRight.Length > 0 ? HelpMark.Right
        : HelpMark.None;

    public static CduRow Empty { get; } = new("", "", new(""), new(""));
}

public enum HelpMark
{
    None,
    Left,
    Right,
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
    PushView? Push = null,
    BatcTrafficModels.Outcome? BatcModels = null,
    FsltlHealth? FsltlHealth = null,
    string? CslNames = null)
{
    /// <summary>BeyondATC won't find FSLTL (or AIG / FS Traffic), so it creates no traffic at all (ADR-0014).</summary>
    public bool BatcNoModels => BatcModels is BatcTrafficModels.Outcome.NoFsltl or BatcTrafficModels.Outcome.NotInMsfs2024;
}

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
    public const string XPlaneSettingsHelp = HelpPage + "#x-plane-settings";

    /// <summary>The page keys under the screen; the getting-started pages are reached from SUPPORT or on first run.</summary>
    public static readonly ImmutableArray<PanelPage> MainPages = [PanelPage.Status, PanelPage.FlightPlan, PanelPage.Push, PanelPage.Setup, PanelPage.Support, PanelPage.About];

    /// <summary>The name on a page key under the screen.</summary>
    public static string KeyName(PanelPage page) => page == PanelPage.FlightPlan ? "FPLN" : page.ToString().ToUpperInvariant();

    /// <summary>The getting-started pages in order.</summary>
    public static readonly ImmutableArray<PanelPage> StartPages = [PanelPage.Welcome, PanelPage.StartFolders, PanelPage.StartTraffic, PanelPage.StartModels, PanelPage.StartXPlane, PanelPage.StartDone];

    public static CduPage Build(PanelPage page, PanelState s, string? flash = null) => page switch
    {
        PanelPage.FlightPlan => FlightPlan(s, flash),
        PanelPage.Push => Push(s, flash),
        PanelPage.Setup => Setup(s, flash),
        PanelPage.Support => Support(s, flash),
        PanelPage.About => About(s, flash),
        PanelPage.Welcome => Welcome(flash),
        PanelPage.StartFolders => StartFolders(s, flash),
        PanelPage.StartTraffic => StartTraffic(s, flash),
        PanelPage.StartModels => StartModels(s, flash),
        PanelPage.StartXPlane => StartXPlane(flash),
        PanelPage.StartDone => StartDone(flash),
        PanelPage.Traffic => Traffic(s, flash),
        PanelPage.Fsltl => FsltlPage(s, flash),
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
            Row("X-PLANE", "BEYONDATC", s.XPlaneConnected ? new("CONNECTED", CduColor.Green) : new("WAITING"), batc)
                with { Help = "Whether X-Plane 12 and BeyondATC are talking to ClearanceLink. Both say CONNECTED when all is well." },
            Row("X-PLANE VERSION", "OTHER ADD-ONS", new(s.XPlaneVersion ?? "-"), new(s.OtherAddOns == 0 ? "NONE" : $"{s.OtherAddOns} IGNORED"))
                with { Help = "X-Plane's version. OTHER ADD-ONS counts MSFS add-ons that tried to connect to ClearanceLink; they are ignored." },
            Row("ATC AIRPORT", "SCENERY", new(s.LastAirport ?? "-"), new(s.LastAirportSource ?? ""))
                with { Help = "The airport BeyondATC last asked about, and the X-Plane scenery its gates and taxiways came from." },
            Row("CONNECTION", "SESSION LOG", new(s.Connection == ConnectionMode.Automatic ? "AUTOMATIC" : "ALWAYS ON", CduColor.Magenta),
                s.SessionLog ? new("ON", CduColor.Green) : new("OFF", CduColor.Amber), CduKey.GoSetup, CduKey.GoSupport)
                with { Help = "How BeyondATC finds ClearanceLink (change it on SETUP), and whether this session is logged (SUPPORT)." },
            Row("RUNNING FOR", Traffic(s).Text == UpdateModels ? "FSLTL MODELS" : "TRAFFIC", new(Duration(s.Uptime)), Traffic(s), CduKey.None, s switch
            {
                { TrafficAircraft: not null, PluginModels: 0 } => CduKey.GoModels,
                { BatcNoModels: true, TrafficAircraft: null or 0 } => CduKey.GoTraffic,
                { FsltlFolder: not null, FsltlHealth.NoModels: true, TrafficAircraft: null or 0 } => CduKey.GoFsltl,
                { FsltlOutOfDate: true, FsltlConverting: false } => CduKey.GoModels,
                { TrafficAircraft: null } => CduKey.GoTraffic,
                _ => CduKey.None,
            }) with { Help = "How long ClearanceLink has been open, and how many of BeyondATC's aircraft are drawn in X-Plane." },
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

        // BATC cleared the pushback: the PUSH page carries it out when the pilot presses START PUSH. It comes only
        // once everything is connected, so it takes the place of READY FOR CLEARANCE.
        if (s.Push is { Clearance: not null, Running: false, BetterPushback: not false } && advice.Color == CduColor.Green)
        {
            return new("PUSHBACK APPROVED: PUSH", CduColor.Green);
        }

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
                p?.CruiseFeet is { } feet ? new($"FL{feet / 100:000}") : None())
                with { Help = "Your latest SimBrief plan: from, to and cruise level. It's the same plan BeyondATC flies." },
            Row("RWY/SID", "STAR/RWY", p is null ? None() : new($"{p.OriginRunway ?? "-"}/{p.Sid ?? "-"}"),
                p is null ? None() : new($"{p.Star ?? "-"}/{p.DestinationRunway ?? "-"}"))
                with { Help = "Departure runway and SID, arrival STAR and runway, as SimBrief planned them." },
            Row("ROUTE", "", p?.Route is { } route ? new(Fit(route.StartsWith("DCT ", StringComparison.Ordinal) ? route[4..] : route, Width)) : None(), new(""))
                with { Help = "The route between the SID and the STAR." },
            Row("MADE", "FMS", p?.Generated is { } made ? new(Zulu(made)) : None(), fms)
                with { Help = "When SimBrief made the plan, and whether it's in X-Plane's FMS yet." },
            Row("", "", new(view.Requesting ? " REQUESTING" : "<REQUEST"),
                p is null ? new("") : s.LoadArmed ? new("CONFIRM>", CduColor.Amber) : new("LOAD>"),
                view.Requesting ? CduKey.None : CduKey.RequestPlan, p is null ? CduKey.None : CduKey.LoadPlan)
                with { Help = "REQUEST fetches your latest SimBrief plan to check here. LOAD puts it in X-Plane's FMS; press it twice, as it replaces the FMS's plan." },
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

    /// <summary>
    /// The pushback page: BeyondATC's clearance ("FACE EAST"), the push it plans, keys to turn the tail another way,
    /// and the tug: AUTO calls Better Pushback's tug when BeyondATC approves (as it calls GSX in MSFS), MANUAL waits
    /// for CALL TUG (ADR-0013). Either way the push only moves once the pilot releases the parking brake.
    /// </summary>
    private static CduPage Push(PanelState s, string? flash)
    {
        var v = s.Push ?? new PushView();
        CduCell Choice(string text, PushChoice choice) => new(text, v.Choice == choice ? CduColor.Magenta : CduColor.White);
        var cleared = v.Clearance is { } c
            ? new CduCell(c.FaceWord is { } w ? $"FACE {w}" : "APPROVED", v.Choice is null ? CduColor.Green : CduColor.White)
            : new CduCell("-----");
        var plan = v.Preview is { } p
            ? new CduCell(p.Turns ? $"{p.StraightMeters:0}M, TAIL {(p.TailLeft ? "LEFT" : "RIGHT")}" : $"{p.StraightMeters:0}M, NO TURN")
            : new CduCell("-----");
        var asCleared = v is { Choice: not null, Clearance: not null };
        var callTug = v.CanStart && !v.Running;
        var rows = new[]
        {
            Row("ATC CLEARANCE", "AT", cleared, new(v.Clearance is { } at ? Zulu(at.At) : ""))
                with { Help = "BeyondATC's pushback approval: the way to face after the push, and when it was given." },
            Row("PUSH BACK", "THEN FACE", plan, new(v.Preview is { } f ? $"{f.FinalHeading:000}" + (f.Taxiway is { } t ? $" ON {t}" : "") : ""))
                with { Help = "How far the tug pushes, which way the tail swings, and the heading you end up on, lined up with the taxiway behind you when there is one." },
            Row("TURN", "", Choice("<TAIL LEFT", PushChoice.TailLeft), Choice("TAIL RIGHT>", PushChoice.TailRight), CduKey.PushTailLeft, CduKey.PushTailRight)
                with { Help = "Push another way than ATC said. TAIL LEFT swings the tail left, so the nose ends up pointing right; TAIL RIGHT the opposite. Works until the tug starts moving." },
            Row("", "", Choice("<NO TURN", PushChoice.Straight), new(asCleared ? "AS ATC SAID>" : ""), CduKey.PushStraight, asCleared ? CduKey.PushAsCleared : CduKey.None)
                with { Help = "NO TURN pushes straight back, about one aircraft length. AS ATC SAID goes back to BeyondATC's direction." },
            Row("TUG", "",
                new(v.Automatic ? "<AUTO" : " AUTO", v.Automatic ? CduColor.Magenta : CduColor.White),
                new(v.Automatic ? "MANUAL" : "MANUAL>", v.Automatic ? CduColor.White : CduColor.Magenta),
                CduKey.PushAuto, CduKey.PushManual)
                with { Help = "AUTO calls Better Pushback's tug as soon as BeyondATC approves your pushback, like GSX in MSFS. MANUAL waits for you to press CALL TUG." },
            Row("", "", new(callTug ? "<CALL TUG" : ""), new(v.Running ? "STOP>" : "", CduColor.Amber),
                callTug ? CduKey.PushStart : CduKey.None, v.Running ? CduKey.PushStop : CduKey.None)
                with { Help = "CALL TUG brings Better Pushback's tug. It pushes once you release the parking brake, and disconnects once you set it again. STOP ends the push where you are." },
        };
        return new($"{AppName} PUSH", rows, flash is null ? PushLine(v) : new(flash));
    }

    /// <summary>The PUSH page's bottom line: where the push is, or what to do next.</summary>
    public static CduCell PushLine(PushView v) => v switch
    {
        { Stage: PushStage.Failed, Problem: { } why } => new(Fit(why, Width), CduColor.Amber),
        { Stage: PushStage.CallingTug } => new("TUG ON THE WAY"),
        { Stage: PushStage.WaitingForBrake, Stalled: true } => new("NOT MOVING: PRESS STOP", CduColor.Amber),
        { Stage: PushStage.WaitingForBrake, BrakeSet: true } => new("RELEASE PARKING BRAKE", CduColor.Amber),
        { Stage: PushStage.WaitingForBrake } => new("PUSH STARTING"),
        { Stage: PushStage.PushingBack } => new($"PUSHING BACK {v.Pushed:0}M", CduColor.Green),
        { Stage: PushStage.Turning, Preview: { } p } => new($"TURNING TAIL {(p.TailLeft ? "LEFT" : "RIGHT")}", CduColor.Green),
        { Stage: PushStage.Straightening } => new("STRAIGHTENING", CduColor.Green),
        { Stage: PushStage.Stopping } => new("STOPPING"),
        { Stage: PushStage.Disconnecting, BrakeSet: true } => new("DISCONNECTING TUG"),
        { Stage: PushStage.Disconnecting } => new("SET PARKING BRAKE", CduColor.Amber),
        { Stage: PushStage.Done } => new("PUSHBACK COMPLETE", CduColor.Green),
        { XPlane: false } => new("START X-PLANE 12"),
        { BetterPushback: false } => new("BETTER PUSHBACK MISSING", CduColor.Amber),
        { Selected: null } => new("WAIT FOR PUSH CLEARANCE"),
        { Automatic: true, Clearance: not null, Choice: null, BetterPushback: null } => new("CHECKING BETTER PUSHBACK"),
        { Automatic: true } => new("TUG COMES WHEN CLEARED"),
        _ => new("PRESS CALL TUG"),
    };

    private static string Zulu(DateTimeOffset at) => at.UtcDateTime.ToString("HHmm'Z'", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>SETUP's bottom line while connection automatic has BeyondATC pointed at ClearanceLink.</summary>
    public const string BatcUntilClose = "BATC: X-PLANE TILL EXIT";

    private static CduPage Setup(PanelState s, string? flash)
    {
        var automatic = s.Connection == ConnectionMode.Automatic;
        var rows = new[]
        {
            Folder("BEYONDATC FOLDER", s.BatcFolder, CduKey.ChooseBatcFolder)
                with { Help = "Where BeyondATC is installed. Press to choose another folder. Nothing in it is changed." },
            Folder("X-PLANE FOLDER", s.XPlaneFolder, CduKey.ChooseXPlaneFolder)
                with { Help = "Where X-Plane 12 is installed, for airports and the traffic plugin. Press to choose another folder." },
            Row("CONNECTION", "",
                new(automatic ? "<AUTOMATIC" : " AUTOMATIC", automatic ? CduColor.Magenta : CduColor.White),
                new(automatic ? "ALWAYS ON" : "ALWAYS ON>", automatic ? CduColor.White : CduColor.Magenta),
                CduKey.ConnectionAutomatic, CduKey.ConnectionAlwaysOn)
                with { Help = "AUTOMATIC: BeyondATC uses X-Plane while this window is open, and MSFS once it's closed. ALWAYS ON: BeyondATC always looks for X-Plane, even with the window closed." },
            Row("START", "WEB STATUS", new(s.BatcFolder is null ? "" : "<BEYONDATC"), new("OPEN>"),
                s.BatcFolder is null ? CduKey.None : CduKey.StartBatc, CduKey.OpenStatusPage)
                with { Help = "Start BeyondATC, or open the same status as a page in your browser." },
            Row("TRAFFIC", "", SetupTraffic(s), new("SET UP>"), CduKey.GoTraffic, CduKey.GoTraffic)
                with { Help = "BeyondATC's traffic in X-Plane: the plugin that draws it, the aircraft models and X-Plane's settings." },
            Row("", "", new("<WELCOME"), new("ABOUT>"), CduKey.ShowGettingStarted, CduKey.GoAbout)
                with { Help = "WELCOME shows the six set-up pages again. ABOUT shows the version." },
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
                CduKey.SessionLogOn, CduKey.SessionLogOff)
                with { Help = "Keep a log of each session, so a support file can show what happened." },
            Row("THIS SESSION", "KEPT FOR", new(s.SessionLog ? Size(s.SessionLogBytes) : "NOT LOGGED"), new($"{s.KeepDays} DAYS"))
                with { Help = "Size of this session's log, and how long logs and recordings are kept." },
            Row("SEND TO NODEBLUE", "", new("<CREATE SUPPORT FILE"), new(""), CduKey.CreateSupportFile)
                with { Help = "Makes a zip of recent logs and settings to send when something goes wrong. Your Windows user name is blanked out." },
            Row("DETAILS", "LOG FILES", new("<LIVE LOG"), new("OPEN FOLDER>"), CduKey.ShowLiveLog, CduKey.OpenLogFolder)
                with { Help = "Watch the log as it's written, or open the folder that holds the logs." },
            Row("", "HELP", new(""), new("OPEN PAGE>"), CduKey.None, CduKey.OpenHelp)
                with { Help = "Opens the help page in your browser." },
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
                ? Row("", "NEW VERSION", new(""), new($"{update.Version}>", CduColor.Magenta), CduKey.None, CduKey.OpenUpdatePage)
                : CduRow.Empty,
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

    private static CduRow FsltlRow(PanelState s) => s switch
    {
        { FsltlConverting: true } => Row("FSLTL", "", new(Progress(s.FsltlProgress), CduColor.Amber), new("")),
        // Liveries but not one base model: nothing can be converted; FSLTL CHECK says why.
        { FsltlFolder: not null, FsltlHealth.NoModels: true } => Row("FSLTL", "", new("NO BASE MODELS", CduColor.Amber), new("CHECK>"), CduKey.GoFsltl, CduKey.GoFsltl),
        { FsltlLiveries: > 0, FsltlOutOfDate: true, FsltlFolder: not null } =>
            Row("FSLTL", "", new("UPDATE NEEDED", CduColor.Amber), new("UPDATE>"), CduKey.None, CduKey.ConvertFsltl),
        // Converted, but the FSLTL folder isn't known (MSFS gone, or never chosen): CHOOSE, so BeyondATC can be shown it (ADR-0014).
        { FsltlLiveries: > 0 } => Row("FSLTL", "", new($"{s.FsltlLiveries} LIVERIES", CduColor.Green), new(s.FsltlFolder is null ? "CHOOSE>" : "UPDATE>"),
            CduKey.None, s.FsltlFolder is null ? CduKey.ChooseFsltlFolder : CduKey.ConvertFsltl),
        { FsltlFolder: not null } => Row("FSLTL", "", new(s.MsfsInstalled ? "FOUND IN MSFS" : "FOUND", CduColor.Green), new("CONVERT>"), CduKey.None, CduKey.ConvertFsltl),
        // No MSFS on this PC: FSLTL can still be copied from another PC, so the folder can still be chosen.
        { MsfsInstalled: false } => Row("FSLTL", "", new("NEEDS MSFS"), new("CHOOSE>"), CduKey.ChooseFsltlFolder, CduKey.ChooseFsltlFolder),
        _ => Row("FSLTL", "", new("NOT FOUND"), new("CHOOSE>"), CduKey.ChooseFsltlFolder, CduKey.ChooseFsltlFolder),
    };

    /// <summary>X-Plane CSL model packages (Bluebell, X-CSL, others): what's installed, by name when it fits.</summary>
    private static CduRow CslRow(PanelState s) =>
        Row("CSL MODELS", "", s.CslPackages > 0 ? new(CslText(s), CduColor.Green) : new("NOT FOUND"),
            new("HOW TO>"), CduKey.None, CduKey.OpenModelsHelp);

    private static string CslText(PanelState s) =>
        s.CslNames is { Length: > 0 and <= Width - 8 } names ? names : $"{s.CslPackages} PACKAGES";

    /// <summary>The models pages' bottom line: the last conversion's result, or what to do before converting.</summary>
    private static CduCell ModelsLine(PanelState s, string? flash)
    {
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
        return new(line, line.StartsWith("CONVERT FAILED", StringComparison.Ordinal) ? CduColor.Amber : CduColor.White);
    }

    /// <summary>
    /// Everything for BeyondATC's traffic in one place, reached from SETUP: the X-Plane plugin, the two model
    /// libraries and X-Plane's settings (Ben, 2026-10-06: the welcome pages were the only way back to them).
    /// </summary>
    private static CduPage Traffic(PanelState s, string? flash)
    {
        var rows = new[]
        {
            (TrafficAddOn(s) is { } plugin && plugin != CduRow.Empty
                ? plugin with { LabelRight = "AIRCRAFT", Right = plugin.KeyRight == CduKey.None ? Traffic(s) : plugin.Right }
                : Row("X-PLANE PLUGIN", "", new("NOT IN THIS COPY"), new(""))) with
                { Help = "ClearanceLink's X-Plane plugin draws BeyondATC's traffic. Install or update it with X-Plane closed." },
            CslRow(s) with { Help = "X-Plane aircraft models such as Bluebell or X-CSL (free). Used where there's no converted FSLTL livery. HOW TO shows where to get them." },
            FsltlRow(s) with { Help = "FSLTL: the MSFS traffic models. BeyondATC needs them for any traffic; they're converted for X-Plane from your own copy. Close X-Plane to convert." },
            Row("X-PLANE SETTINGS", "", new("3 TO TURN OFF"), new("HOW TO>"), CduKey.None, CduKey.OpenXPlaneSettingsHelp)
                with { Help = "In X-Plane, set Radios to 0, Draw parked aircraft off and AI aircraft to none, so only BeyondATC talks and flies traffic." },
            Row("BEYONDATC TRAFFIC", "", BatcSees(s), new("CHECK>"), CduKey.GoFsltl, CduKey.GoFsltl)
                with { Help = "BeyondATC creates traffic only when it finds FSLTL. CHECK shows what's in your FSLTL folder and whether BeyondATC sees it." },
            Row("", "", new("<SETUP"), new("STATUS>"), CduKey.GoSetup, CduKey.GoStatus),
        };
        return new($"{AppName} TRAFFIC", rows, ModelsLine(s, flash));
    }

    /// <summary>The TRAFFIC page's BeyondATC line: whether BeyondATC will find FSLTL (or AIG / FS Traffic) at all.</summary>
    private static CduCell BatcSees(PanelState s) => s.BatcModels switch
    {
        BatcTrafficModels.Outcome.Found or BatcTrafficModels.Outcome.Pointed or BatcTrafficModels.Outcome.PointedByAnother => new("SEES FSLTL", CduColor.Green),
        BatcTrafficModels.Outcome.NotInMsfs2024 => new("NOT IN MSFS 2024", CduColor.Amber),
        BatcTrafficModels.Outcome.NoFsltl => new("NO FSLTL", CduColor.Amber),
        BatcTrafficModels.Outcome.Failed => new("CAN'T TELL", CduColor.Amber),
        _ => new("-"),
    };

    /// <summary>
    /// FSLTL CHECK: what the chosen FSLTL folder holds, whether BeyondATC will see it and the X-Plane copy, so a
    /// problem reads as a setup step, not as a ClearanceLink or BeyondATC failure (issue #29).
    /// </summary>
    private static CduPage FsltlPage(PanelState s, string? flash)
    {
        var h = s.FsltlHealth;
        var known = s.FsltlFolder is not null && h is not null;
        var version = h?.Version is { } v ? Fit(v.ToUpperInvariant(), 8) : "-";
        var batc = s.BatcModels switch
        {
            BatcTrafficModels.Outcome.Found => (new CduCell("SEES IT", CduColor.Green), new CduCell("IN MSFS 2024", CduColor.Green)),
            BatcTrafficModels.Outcome.Pointed or BatcTrafficModels.Outcome.PointedByAnother => (new CduCell("SEES IT", CduColor.Green), new CduCell("WHILE OPEN", CduColor.Green)),
            BatcTrafficModels.Outcome.NotInMsfs2024 => (new CduCell("WON'T SEE IT", CduColor.Amber), new CduCell("NOT IN 2024", CduColor.Amber)),
            BatcTrafficModels.Outcome.NoFsltl => (new CduCell("WON'T SEE IT", CduColor.Amber), new CduCell("")),
            BatcTrafficModels.Outcome.Failed => (new CduCell("CAN'T TELL", CduColor.Amber), new CduCell("")),
            _ => (new CduCell("-"), new CduCell("")),
        };
        var rows = new[]
        {
            (s.FsltlFolder is null
                ? Row("FOLDER", "VERSION", new("NOT FOUND", CduColor.Amber), new("-"))
                : Row("FOLDER", "VERSION", new(ShortPath(Parent(s.FsltlFolder), Width - 1 - version.Length)), new(version)))
                with { Help = "Where the fsltl-traffic-base folder ClearanceLink uses is: usually MSFS's Community folder. CHOOSE FOLDER picks another." },
            (known
                ? Row("LIVERIES", "BASE MODELS",
                    h!.Liveries > 0 ? new($"{h.Liveries}", CduColor.Green) : new("NONE", CduColor.Amber),
                    h.Models > 0 ? new($"{h.Models}", h.WithoutModel > 0 ? CduColor.Amber : CduColor.Green) : new("NONE", CduColor.Amber))
                : Row("LIVERIES", "BASE MODELS", new("-"), new("-")))
                with { Help = "FSLTL's liveries and the base aircraft models they use. Every livery needs its base model, in the same folder, to be converted." },
            Row("BEYONDATC", "", batc.Item1, batc.Item2)
                with { Help = "BeyondATC only looks for FSLTL in MSFS 2024's Community folder. Without MSFS 2024, ClearanceLink shows it your FSLTL folder while it's open." },
            CopyRow(s) with { Help = "The copy of FSLTL converted for X-Plane. Close X-Plane to convert or update it." },
            Row("", "", new(""), new("CHOOSE FOLDER>"), CduKey.None, CduKey.ChooseFsltlFolder),
            Row("", "", new("<TRAFFIC"), new(""), CduKey.GoTraffic),
        };
        // A conversion runs in the background: its failure, or the restart it needs, must show here too.
        var line = flash is not null ? new CduCell(flash)
            : s is { FsltlConverting: false, FsltlMessage: { } done } && (done.StartsWith("CONVERT", StringComparison.Ordinal) || done.StartsWith("RESTART", StringComparison.Ordinal))
                ? new CduCell(done, CduColor.Amber)
                : FsltlVerdict(s);
        return new("FSLTL CHECK", rows, line);
    }

    /// <summary>FSLTL CHECK's X-Plane copy: converted, out of date, converting or not yet converted.</summary>
    private static CduRow CopyRow(PanelState s) => s switch
    {
        { FsltlConverting: true } => Row("X-PLANE COPY", "", new(Progress(s.FsltlProgress), CduColor.Amber), new("")),
        { FsltlLiveries: > 0, FsltlOutOfDate: true, FsltlFolder: not null } =>
            Row("X-PLANE COPY", "", new("UPDATE NEEDED", CduColor.Amber), new("UPDATE>"), CduKey.None, CduKey.ConvertFsltl),
        { FsltlLiveries: > 0 } => Row("X-PLANE COPY", "", new($"{s.FsltlLiveries} LIVERIES", CduColor.Green),
            s.FsltlFolder is null ? new("") : new("UPDATE>"), CduKey.None, s.FsltlFolder is null ? CduKey.None : CduKey.ConvertFsltl),
        { FsltlFolder: not null, FsltlHealth: { Models: > 0 } } => Row("X-PLANE COPY", "", new("NOT CONVERTED"), new("CONVERT>"), CduKey.None, CduKey.ConvertFsltl),
        _ => Row("X-PLANE COPY", "", new("-"), new("")),
    };

    /// <summary>FSLTL CHECK's bottom line: the one thing to do, or that FSLTL is ready.</summary>
    private static CduCell FsltlVerdict(PanelState s) => (s, s.FsltlHealth) switch
    {
        ({ FsltlFolder: null }, _) => new("CHOOSE YOUR FSLTL FOLDER", CduColor.Amber),
        // The check runs with the folder lookup, so no result means the folder couldn't be read (a drive gone, no access).
        (_, null) => new("CAN'T READ THIS FOLDER", CduColor.Amber),
        (_, { Liveries: 0 }) => new("NO LIVERIES IN THIS FOLDER", CduColor.Amber),
        (_, { NoModels: true }) => new("LIVERIES BUT NO MODELS", CduColor.Amber),
        ({ BatcModels: BatcTrafficModels.Outcome.NotInMsfs2024 }, _) => new("PUT FSLTL IN MSFS 2024", CduColor.Amber),
        ({ BatcModels: BatcTrafficModels.Outcome.Failed }, _) => new("BEYONDATC CHECK FAILED", CduColor.Amber),
        (_, { WithoutModel: > 0 } h) => new(Fit($"{h.WithoutModel} LIVERIES: NO MODEL", Width), CduColor.Amber),
        ({ FsltlConverting: true }, _) => new("KEEP THIS WINDOW OPEN"),
        ({ FsltlLiveries: 0 }, _) => s.XPlaneRunning ? new("CLOSE X-PLANE TO CONVERT") : new("READY TO CONVERT", CduColor.Green),
        ({ FsltlOutOfDate: true }, _) => new(s.XPlaneRunning ? "CLOSE X-PLANE TO UPDATE" : "UPDATE NEEDED", CduColor.Amber),
        _ => new("FSLTL READY", CduColor.Green),
    };

    /// <summary>SETUP's traffic row: what needs doing, else how much traffic is drawn.</summary>
    private static CduCell SetupTraffic(PanelState s) => s switch
    {
        { TrafficAddOn: PluginInstall.State.NotInstalled } => new("INSTALL PLUGIN", CduColor.Amber),
        { TrafficAddOn: PluginInstall.State.Outdated } => new("UPDATE PLUGIN", CduColor.Amber),
        { FsltlFolder: not null, FsltlHealth.NoModels: true, FsltlConverting: false } => new("CHECK FSLTL", CduColor.Amber),
        { FsltlOutOfDate: true, FsltlConverting: false } => new("UPDATE MODELS", CduColor.Amber),
        { FsltlConverting: true } => new("CONVERTING", CduColor.Amber),
        { BatcNoModels: true, TrafficAircraft: null or 0 } => new("NO FSLTL", CduColor.Amber),
        { TrafficAircraft: not null, PluginModels: 0 } => new("NO MODELS", CduColor.Amber),
        { TrafficAircraft: { } n } => new($"{n} AIRCRAFT", CduColor.Green),
        { TrafficAddOn: PluginInstall.State.Installed } => new("READY", CduColor.Green),
        _ => new("-"),
    };

    private static CduPage StartModels(PanelState s, string? flash)
    {
        var rows = new[]
        {
            CduRow.Empty, CduRow.Empty, CduRow.Empty,
            CslRow(s),
            FsltlRow(s),
            StartNav(),
        };
        CduCell[] message =
        [
            Say("Aircraft models", CduColor.Green),
            s.FsltlFolder is null && !s.MsfsInstalled
                ? Say("BeyondATC needs FSLTL for traffic. No MSFS here: CHOOSE your FSLTL folder.")
                : Say("BeyondATC needs FSLTL for traffic. It's converted from your MSFS copy; CSL models are optional."),
        ];
        return new(StartTitle(PanelPage.StartModels), rows, ModelsLine(s, flash), message);
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
        return new(StartTitle(PanelPage.StartDone), rows, new(flash ?? "REOPEN ON SETUP PAGE"), message);
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
        // BeyondATC won't find FSLTL, so it creates no traffic at all (ADR-0014): the key opens TRAFFIC.
        { BatcNoModels: true, TrafficAircraft: null or 0 } => new("NO FSLTL", CduColor.Amber),
        // FSLTL changed since the models were converted: the key opens the aircraft models page, which has UPDATE.
        // While aircraft are drawn the count stays (the old models still work); the key still opens that page.
        // Liveries but no base models: an UPDATE would convert nothing, so this comes before it; the key opens FSLTL CHECK.
        { FsltlFolder: not null, FsltlHealth.NoModels: true, TrafficAircraft: null or 0 } => new("CHECK FSLTL", CduColor.Amber),
        { FsltlOutOfDate: true, FsltlConverting: false, TrafficAircraft: null or 0 } => new(UpdateModels, CduColor.Amber),
        { TrafficAircraft: { } n } => new($"{n} AIRCRAFT", CduColor.Green),
        { TrafficAddOn: PluginInstall.State.NotInstalled or PluginInstall.State.Outdated } => new("SET UP>", CduColor.Amber),
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
    /// <summary>The folder a Windows path is in (split by hand, as in <see cref="ShortPath"/>).</summary>
    private static string Parent(string path)
    {
        path = path.TrimEnd('\\', '/');
        var cut = path.LastIndexOfAny(['\\', '/']);
        return cut > 0 ? path[..cut] : path;
    }

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

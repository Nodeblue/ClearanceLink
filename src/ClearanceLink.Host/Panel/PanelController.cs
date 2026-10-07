using System.Diagnostics;
using ClearanceLink.Host.Status;
using ClearanceLink.Host.Support;
using ClearanceLink.Host.Traffic;
using ClearanceLink.Host.Transport;

namespace ClearanceLink.Host.Panel;

/// <summary>
/// Runs the server in the background while the window is open, gathers what the pages show, and carries out the
/// line select keys that don't need the window itself (folder pickers and the live log are the window's).
/// </summary>
public sealed class PanelController : IAsyncDisposable
{
    private static readonly TimeSpan FolderRefresh = TimeSpan.FromSeconds(10);

    /// <summary>How often the X-Plane plugin's files are compared with the bundled ones (it hashes every file).</summary>
    private static readonly TimeSpan PluginRecheck = TimeSpan.FromMinutes(5);

    private readonly Lock _lock = new();
    private readonly DateTime _started = DateTime.Now;
    private CancellationTokenSource? _stop;
    private Task? _serve;
    private StatusBoard? _board;
    private DateTimeOffset _loadArmedUntil = DateTimeOffset.MinValue;
    private volatile Found _folders = new();
    private Task? _refresh;
    private volatile bool _refreshDue = true;
    private volatile PluginCheck? _plugin;
    private bool _foldersChanged;
    private volatile bool _fsltlConverting;
    private volatile string? _fsltlMessage;
    private volatile Fsltl.ImportProgress? _fsltlProgress;
    private CancellationTokenSource? _fsltlCancel;
    private Task? _fsltl;

    public PanelController()
    {
        // Looked up in the background from the start, so the first page already shows the folders.
        RefreshFolders();
    }

    public void Start()
    {
        lock (_lock)
        {
            _stop = new CancellationTokenSource();
            _board = null;
            var stop = _stop.Token;
            _serve = Task.Run(async () =>
            {
                try
                {
                    await Cli.RunServerAsync("serve", Options.Parse(["--quiet"]), board => _board = board, stop).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The page would only say STARTING... for ever; the reason goes in the log (LIVE LOG on SUPPORT).
                    PacketConsole.Info($"panel: server stopped: {ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}");
                    _board = null;
                }
            });
        }
    }

    public async Task RestartAsync()
    {
        await StopAsync().ConfigureAwait(false);
        Start();
    }

    public async ValueTask DisposeAsync()
    {
        // A conversion stopped by closing the window finishes the texture it is on, then leaves the old package.
        StopConverting();
        if (_fsltl is { } fsltl)
        {
            try
            {
                await fsltl.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                PacketConsole.Info("fsltl: conversion did not stop within 20 s");
            }
        }

        await StopAsync().ConfigureAwait(false);
    }

    /// <summary>Stops a running FSLTL conversion (the window is closing); the package converted before stays as it was.</summary>
    public void StopConverting()
    {
        lock (_lock)
        {
            if (_fsltlConverting && _fsltlCancel is { IsCancellationRequested: false } cancel)
            {
                PacketConsole.Info("fsltl: stopping the conversion (window closed)");
                cancel.Cancel();
            }
        }
    }

    /// <summary>What the pages show. Called every second on the window's thread, so it never waits for the disk.</summary>
    public PanelState State()
    {
        var settings = AppSettings.Load(AppFolders.Default.Settings);
        if (_folders.At == default)
        {
            // Only before the very first lookup has finished (the window opening): wait for it rather than show
            // every folder as NOT FOUND for a moment.
            RefreshFolders(due: false).Wait(TimeSpan.FromSeconds(3));
        }

        var found = Folders();
        var (batc, xplane) = (found.Batc, found.XPlane);
        var addOn = found.AddOn;
        var snapshot = _board?.Snapshot();
        var names = snapshot?.Clients.Select(c => c.Name).ToList() ?? [];
        var airport = snapshot is { Airports.Count: > 0 } ? snapshot.Airports[0] : null;
        var log = SessionLog.Path;
        return new PanelState(
            Cli.Version,
            Serving: snapshot is not null,
            XPlaneConnected: snapshot?.XPlane.Connected ?? false,
            XPlaneVersion: snapshot?.XPlane.Version,
            BatcHelper: names.Any(StatusBoard.IsAirportClient),
            BatcApp: StatusBoard.HasApp(names),
            OtherAddOns: names.Count(n => n != "" && !StatusBoard.IsBatcClient(n)),
            LastAirport: airport?.Icao,
            LastAirportSource: airport?.Source,
            MsfsRunning: snapshot?.MsfsRunning ?? false,
            settings.Connection,
            BatcRedirected: batc is not null && new BatcRedirect(batc).IsOn,
            settings.SessionLog,
            SessionLogBytes: log is not null && File.Exists(log) ? new FileInfo(log).Length : 0,
            settings.KeepDays,
            DateTime.Now - _started,
            batc,
            xplane,
            addOn,
            snapshot?.Plugin?.Aircraft,
            found.Csl,
            found.Fsltl,
            found.FsltlLiveries,
            _fsltlConverting,
            settings.CheckForUpdates ? UpdateCheck.Result : null,
            snapshot?.Plugin?.Models,
            _fsltlMessage,
            found.FsltlOutOfDate,
            _fsltlProgress,
            found.XPlaneRunning,
            found.MsfsInstalled,
            _board?.FlightPlans?.View,
            LoadArmed: DateTimeOffset.UtcNow < _loadArmedUntil,
            Push: _board?.Pushbacks?.View,
            BatcModels: found.BatcModels,
            FsltlHealth: found.FsltlHealth,
            CslNames: found.CslNames,
            BatcCommunity: found.BatcCommunity,
            FsltlChecking: found.At < _fsltlChosenAt,
            BatcTrafficSync: names.Contains(StatusBoard.TrafficSyncClient));
    }

    /// <summary>Carries out a key; returns a short line for the scratchpad, or null.</summary>
    public async Task<string?> DoAsync(CduKey key)
    {
        switch (key)
        {
            case CduKey.RequestPlan:
                _loadArmedUntil = DateTimeOffset.MinValue;
                return _board?.FlightPlans is { } request ? await request.RequestAsync().ConfigureAwait(false) : "STARTING...";
            case CduKey.LoadPlan:
                // Loading replaces whatever is in the FMS: the first press asks, a second within 10 s loads.
                if (DateTimeOffset.UtcNow >= _loadArmedUntil)
                {
                    _loadArmedUntil = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
                    return null;
                }

                _loadArmedUntil = DateTimeOffset.MinValue;
                return _board?.FlightPlans?.Load() ?? "STARTING...";
            case CduKey.PushTailLeft:
            case CduKey.PushTailRight:
            case CduKey.PushStraight:
            case CduKey.PushAsCleared:
                return _board?.Pushbacks?.Choose(key switch
                {
                    CduKey.PushTailLeft => Pushback.PushChoice.TailLeft,
                    CduKey.PushTailRight => Pushback.PushChoice.TailRight,
                    CduKey.PushStraight => Pushback.PushChoice.Straight,
                    _ => null,
                }) is { Length: > 0 } chosen ? chosen : null;
            case CduKey.PushStart:
                return _board?.Pushbacks?.Start() ?? "STARTING...";
            case CduKey.PushStop:
                return _board?.Pushbacks?.Stop() ?? "STARTING...";
            case CduKey.PushAuto:
            case CduKey.PushManual:
                return SetPushAutomatic(key == CduKey.PushAuto);
            case CduKey.ConnectionAutomatic:
            case CduKey.ConnectionAlwaysOn:
                return await SetConnectionAsync(key == CduKey.ConnectionAutomatic ? ConnectionMode.Automatic : ConnectionMode.AlwaysOn).ConfigureAwait(false);
            case CduKey.OpenStatusPage:
                Cli.OpenInBrowser($"http://localhost:{StatusServer.DefaultPort}/");
                return "OPENING STATUS PAGE";
            case CduKey.StartBatc:
                return StartBatc();
            case CduKey.SessionLogOn:
            case CduKey.SessionLogOff:
                return SetSessionLog(key == CduKey.SessionLogOn);
            case CduKey.CreateSupportFile:
                var zip = await Task.Run(Cli.CreateSupportFile).ConfigureAwait(false);
                Cli.ShowInExplorer(zip);
                PacketConsole.Info($"support: {zip}");
                return "SUPPORT FILE SAVED";
            case CduKey.OpenLogFolder:
                Directory.CreateDirectory(AppFolders.Default.Logs);
                Cli.ShowInExplorer(AppFolders.Default.Logs);
                return "OPENING LOG FOLDER";
            case CduKey.OpenHelp:
                Cli.OpenInBrowser(CduPages.HelpPage);
                return "OPENING HELP PAGE";
            case CduKey.OpenModelsHelp:
                Cli.OpenInBrowser(CduPages.ModelsHelp);
                return "OPENING HELP PAGE";
            case CduKey.OpenXPlaneSettingsHelp:
                Cli.OpenInBrowser(CduPages.XPlaneSettingsHelp);
                return "OPENING HELP PAGE";
            case CduKey.ConvertFsltl:
                return await ConvertFsltlAsync().ConfigureAwait(false);
            case CduKey.InstallTrafficAddOn:
                return InstallTrafficAddOn();
            case CduKey.OpenUpdatePage:
                Cli.OpenInBrowser(UpdateCheck.Available?.PageUrl ?? UpdateCheck.ReleasesPage);
                return "OPENING DOWNLOAD PAGE";
            case CduKey.OpenWebsite:
                Cli.OpenInBrowser(CduPages.Website);
                return "OPENING WEBSITE";
            default:
                return null;
        }
    }

    public const string FsltlSaved = "FSLTL FOLDER SAVED";

    /// <summary>Saves a folder the user picked. Returns the scratchpad line.</summary>
    public string SetFolder(CduKey which, string folder)
    {
        var settings = AppSettings.Load(AppFolders.Default.Settings);
        if (which == CduKey.ChooseFsltlFolder)
        {
            if (Fsltl.FsltlLocator.Find(folder) is null)
            {
                return "NOT AN FSLTL FOLDER";
            }

            // The package itself, found from the Community folder or a folder inside it.
            settings.FsltlFolder = Path.GetFullPath(Fsltl.FsltlLocator.Find(folder)!);
            settings.Save(AppFolders.Default.Settings);
            // Choosing the folder again (after repairing FSLTL, as help says) checks it again.
            _health = null;
            _fsltlChosenAt = DateTime.Now;
            RefreshFolders();
            return FsltlSaved;
        }

        if (which == CduKey.ChooseBatcFolder)
        {
            if (BatcRedirect.FindInstallFolder(folder) is not { } found)
            {
                return "NO BEYONDATC.EXE THERE";
            }

            settings.BatcFolder = found;
        }
        else
        {
            if (Airports.XPlaneInstall.Find(folder) is null)
            {
                return "NOT AN X-PLANE FOLDER";
            }

            settings.XPlaneFolder = Path.GetFullPath(folder);
        }

        settings.Save(AppFolders.Default.Settings);
        RefreshFolders();
        _foldersChanged = true;
        return which == CduKey.ChooseBatcFolder ? "BEYONDATC FOLDER SAVED" : "X-PLANE FOLDER SAVED";
    }

    private async Task<string> SetConnectionAsync(ConnectionMode mode)
    {
        var settings = AppSettings.Load(AppFolders.Default.Settings);
        if (settings.Connection == mode)
        {
            return mode == ConnectionMode.Automatic ? "CLOSE MSFS BEFORE FLYING" : "BATC SET TO X-PLANE";
        }

        settings.Connection = mode;
        if (Cli.ApplyConnection(settings, Folders().Batc) is { } problem)
        {
            // A read-only BeyondATC folder (under Program Files) needs administrator rights to take the two files.
            PacketConsole.Info($"panel: connection not changed: {problem}");
            return problem == Cli.BatcNotFoundProblem ? "SET BEYONDATC FOLDER" : "RUN AS ADMINISTRATOR";
        }

        settings.Save(AppFolders.Default.Settings);
        var batcWasConnected = State().BatcSeen;
        await RestartAsync().ConfigureAwait(false);
        return batcWasConnected ? "NOW RESTART BEYONDATC" : mode == ConnectionMode.Automatic ? "AUTOMATIC SELECTED" : "ALWAYS ON SELECTED";
    }

    private string StartBatc()
    {
        if (Folders().Batc is not { } folder)
        {
            return "SET BEYONDATC FOLDER";
        }

        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(folder, "BeyondATC.exe")) { UseShellExecute = true, WorkingDirectory = folder })?.Dispose();
            return "STARTING BEYONDATC";
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            PacketConsole.Info($"panel: could not start BeyondATC: {ex.Message}");
            return "COULD NOT START BATC";
        }
    }

    /// <summary>TUG AUTO / MANUAL on the PUSH page: saved, and used from the next clearance on.</summary>
    private string? SetPushAutomatic(bool automatic)
    {
        if (_board?.Pushbacks is not { } pushbacks)
        {
            return "STARTING...";
        }

        var settings = AppSettings.Load(AppFolders.Default.Settings);
        settings.PushAutomatic = automatic;
        settings.Save(AppFolders.Default.Settings);
        pushbacks.Automatic = settings.PushAutomatic;
        PacketConsole.Info($"panel: pushback start {(settings.PushAutomatic ? "auto" : "manual")}");
        return null;
    }

    /// <summary>Saves that the getting-started pages are done; restarts the server when a folder was chosen meanwhile.</summary>
    public async Task<string> FinishGettingStartedAsync()
    {
        var settings = AppSettings.Load(AppFolders.Default.Settings);
        settings.GettingStartedDone = true;
        settings.Save(AppFolders.Default.Settings);
        if (_foldersChanged && !State().BatcSeen)
        {
            _foldersChanged = false;
            await RestartAsync().ConfigureAwait(false);
        }

        return "SET UP DONE";
    }

    /// <summary>The CSL folders the traffic add-on searches (plugin/src/CslFolders.cpp), relative to X-Plane's folder.</summary>
    private static readonly string[] CslFolders =
    [
        @"Resources\plugins\ClearanceLink\Resources\CSL",
        @"Custom Data\CSL",
        @"Resources\plugins\LiveTraffic\Resources\CSL",
        @"Resources\plugins\xPilot\Resources\CSL",
        @"Resources\plugins\X-IvAp Resources\CSL",
        @"Resources\plugins\swift\Resources\CSL",
    ];

    /// <summary>CSL packages (folders with an xsb_aircraft.txt, up to three levels down) where the traffic add-on looks.</summary>
    public static int CountCslPackages(string xplaneRoot, string? except = null) => CslPackages(xplaneRoot, except).Count;

    /// <summary>The CSL packages' folders, relative to X-Plane's folder.</summary>
    public static List<string> CslPackages(string xplaneRoot, string? except = null)
    {
        var found = new List<string>();
        foreach (var folder in CslFolders.Select(f => Path.Combine([xplaneRoot, .. f.Split('\\')])).Where(Directory.Exists))
        {
            FindPackages(folder, depth: 3, except is null ? null : Path.GetFullPath(except), found);
        }

        return [.. found.Select(f => Path.GetRelativePath(xplaneRoot, f))];
    }

    /// <summary>
    /// What the CSL packages are, for the TRAFFIC page: "BLUEBELL + X-CSL". Bluebell's packages are named BB_*
    /// (BB_Airbus, BB_Boeing1...); X-CSL's sit in a folder named X-CSL (its updater's and swift's default); anything
    /// else is OTHER. Null when there are none.
    /// </summary>
    public static string? CslNames(IEnumerable<string> packages)
    {
        var kinds = new SortedSet<int>();
        foreach (var package in packages)
        {
            var parts = package.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            kinds.Add(parts.Any(p => p.Contains("X-CSL", StringComparison.OrdinalIgnoreCase)) ? 1
                : parts.Any(p => p.StartsWith("BB_", StringComparison.OrdinalIgnoreCase) || p.Contains("Bluebell", StringComparison.OrdinalIgnoreCase)) ? 0
                : 2);
        }

        return kinds.Count == 0 ? null : string.Join(" + ", kinds.Select(k => k switch { 0 => "BLUEBELL", 1 => "X-CSL", _ => "OTHER" }));
    }

    private static void FindPackages(string folder, int depth, string? except, List<string> found)
    {
        if (except is not null && string.Equals(Path.GetFullPath(folder), except, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (File.Exists(Path.Combine(folder, "xsb_aircraft.txt")))
        {
            found.Add(folder);
            return;
        }

        try
        {
            if (depth > 0)
            {
                foreach (var d in Directory.GetDirectories(folder)) FindPackages(d, depth - 1, except, found);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable folder holds no packages we can use.
        }
    }

    private string InstallTrafficAddOn()
    {
        if (Folders().XPlane is not { } xplane)
        {
            return "SET X-PLANE FOLDER";
        }

        // X-Plane keeps the plugin file open while it runs (also before its Web API answers).
        if (_board?.Snapshot().XPlane.Connected == true || Process.GetProcessesByName("X-Plane").Length > 0)
        {
            return "CLOSE X-PLANE FIRST";
        }

        var install = new PluginInstall(xplane);
        try
        {
            install.Install().ToList().ForEach(l => PacketConsole.Info($"plugin: {l}"));
            _plugin = null;
            RefreshFolders();
            return "PLUGIN INSTALLED";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PacketConsole.Info($"plugin: could not install: {install.Describe(ex)}");
            return PluginInstall.Classify(ex) switch
            {
                PluginInstall.Failure.XPlaneRunning => "CLOSE X-PLANE FIRST",
                PluginInstall.Failure.NoPermission => "RUN AS ADMINISTRATOR",
                _ => "INSTALL FAILED: SEE LIVE LOG",
            };
        }
    }

    private static string SetSessionLog(bool on)
    {
        var settings = AppSettings.Load(AppFolders.Default.Settings);
        settings.SessionLog = on;
        settings.Save(AppFolders.Default.Settings);
        if (on)
        {
            SessionLog.Start(AppFolders.Default.Logs, "serve", Cli.Version, DateTime.Now);
        }
        else
        {
            SessionLog.Stop();
        }

        return on ? "SESSION LOG ON" : "SESSION LOG OFF";
    }

    /// <summary>
    /// What was last found on disk. It is looked up again in the background every 10 s and after a change (the
    /// registry search, the plugin check, the CSL folder walk and the FSLTL catalogue take too long for the window's
    /// thread); until then this returns the last result.
    /// </summary>
    private Found Folders()
    {
        if (_refreshDue || DateTime.Now - _folders.At > FolderRefresh)
        {
            RefreshFolders(due: false);
        }

        return _folders;
    }

    /// <summary>Starts a lookup unless one is running; with <paramref name="due"/>, another runs after it (something changed meanwhile).</summary>
    private Task RefreshFolders(bool due = true)
    {
        lock (_lock)
        {
            if (due)
            {
                _refreshDue = true;
            }

            if (_refresh is { IsCompleted: false } running)
            {
                return running;
            }

            _refreshDue = false;
            _refresh = Task.Run(() =>
            {
                try
                {
                    _folders = LookUp();
                }
                catch (Exception ex)
                {
                    // Keep the last result; the next refresh tries again.
                    PacketConsole.Info($"panel: folder lookup failed: {ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}");
                    _folders = _folders with { At = DateTime.Now };
                }
            });
            return _refresh;
        }
    }

    /// <summary>A lookup that has started after this call and finished: for keys that act on the folders.</summary>
    private async Task<Found> FreshFoldersAsync()
    {
        await RefreshFolders().ConfigureAwait(false);
        if (_refreshDue)
        {
            // The lookup awaited had started before this call; the one queued behind it has not.
            await RefreshFolders(due: false).ConfigureAwait(false);
        }

        return _folders;
    }

    private sealed record PluginCheck(string XPlane, PluginInstall.State State, DateTime At);

    private Found LookUp()
    {
        // When it started: a folder chosen meanwhile isn't in this result (FsltlChecking).
        var started = DateTime.Now;
        var none = new Options();
        var xplane = Cli.FindXPlane(none)?.Root;
        var fsltl = Cli.FsltlFolder(none);
        var output = xplane is null ? null : Cli.FsltlOutput(xplane);
        var csl = xplane is null ? [] : CslPackages(xplane, except: output);
        return new Found
        {
            Batc = Cli.BatcFolder(none),
            XPlane = xplane,
            AddOn = PluginState(xplane),
            Csl = csl.Count,
            CslNames = CslNames(csl),
            FsltlHealth = fsltl is null ? null : Health(fsltl),
            Fsltl = fsltl,
            FsltlLiveries = output is null ? 0 : Fsltl.FsltlImporter.LoadCatalogue(output).Count,
            FsltlOutOfDate = fsltl is not null && output is not null && OutOfDate(fsltl, output),
            XPlaneRunning = XPlaneRunning(),
            MsfsInstalled = Fsltl.FsltlLocator.MsfsInstalled(),
            BatcModels = OperatingSystem.IsWindows() ? BatcTrafficModels.Default.Check(fsltl) : null,
            BatcCommunity = OperatingSystem.IsWindows() ? BatcTrafficModels.Default.Where().Community : null,
            At = started,
        };
    }

    /// <summary>When an FSLTL folder was last chosen: FSLTL CHECK says CHECKING until a lookup after it has finished.</summary>
    private DateTime _fsltlChosenAt;

    private sealed record HealthCheck(string Folder, string Version, Fsltl.FsltlHealth? Health, DateTime At);

    private volatile HealthCheck? _health;

    /// <summary>A folder with a problem is checked again this often, so a repaired FSLTL shows without a restart.</summary>
    private static readonly TimeSpan HealthRecheck = TimeSpan.FromMinutes(1);

    /// <summary>
    /// What the FSLTL folder holds (FSLTL CHECK). Reading every aircraft.cfg takes a second or two, so a good result
    /// is kept until the folder or FSLTL's version changes or the folder is chosen again; a problem is checked again
    /// every minute. Logged when the result changes.
    /// </summary>
    private Fsltl.FsltlHealth? Health(string fsltl)
    {
        var last = _health;
        try
        {
            var version = Fsltl.FsltlImporter.FsltlVersion(fsltl);
            var problem = last?.Health is null or { NoModels: true } or { Liveries: 0 } or { WithoutModel: > 0 };
            if (last is not null && last.Folder == fsltl && last.Version == version && !(problem && DateTime.Now - last.At >= HealthRecheck))
            {
                return last.Health;
            }

            var health = Fsltl.FsltlCheck.Check(fsltl);
            if (last is null || last.Folder != fsltl || last.Health != health)
            {
                PacketConsole.Info(Fsltl.FsltlCheck.Describe(fsltl, health));
            }

            _health = new HealthCheck(fsltl, version, health, DateTime.Now);
            return health;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The version fingerprint reads the folder too: a drive gone mid-read is CAN'T READ THIS FOLDER, not a failed lookup.
            _health = new HealthCheck(fsltl, "", null, DateTime.Now);
            return null;
        }
    }

    private static bool OutOfDate(string fsltl, string output)
    {
        try
        {
            return Fsltl.FsltlImporter.OutOfDate(fsltl, output) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The plugin check hashes every bundled file: done again only for another X-Plane, after an install, or every 5 minutes.</summary>
    private PluginInstall.State PluginState(string? xplane)
    {
        if (xplane is null)
        {
            return PluginInstall.State.NotBundled;
        }

        if (_plugin is { } last && last.XPlane == xplane && DateTime.Now - last.At < PluginRecheck)
        {
            return last.State;
        }

        var state = new PluginInstall(xplane).Check();
        _plugin = new PluginCheck(xplane, state, DateTime.Now);
        return state;
    }

    private static bool XPlaneRunning()
    {
        var processes = Process.GetProcessesByName("X-Plane");
        foreach (var p in processes)
        {
            p.Dispose();
        }

        return processes.Length > 0;
    }

    /// <summary>Converts the user's FSLTL models into X-Plane's CSL folder in the background (it takes minutes).</summary>
    private async Task<string> ConvertFsltlAsync()
    {
        var found = await FreshFoldersAsync().ConfigureAwait(false);
        if (found.Fsltl is not { } package || found.XPlane is not { } xplane)
        {
            return found.XPlane is null ? "SET X-PLANE FOLDER" : "CHOOSE THE FSLTL FOLDER";
        }

        // Replacing files X-Plane has open can leave it drawing a mix of old and new models.
        if (XPlaneRunning())
        {
            return "CLOSE X-PLANE FIRST";
        }

        CancellationToken cancel;
        lock (_lock)
        {
            if (_fsltlConverting)
            {
                return "ALREADY CONVERTING";
            }

            _fsltlConverting = true;
            _fsltlMessage = null;
            _fsltlProgress = null;
            _fsltlCancel?.Dispose();
            _fsltlCancel = new CancellationTokenSource();
            cancel = _fsltlCancel.Token;
        }

        _fsltl = Task.Run(async () =>
        {
            try
            {
                var result = Fsltl.FsltlImporter.Import(new Fsltl.ImportOptions
                {
                    PackageRoot = package,
                    Output = Cli.FsltlOutput(xplane, moveOld: true),
                    Log = line => PacketConsole.Info($"fsltl: {line}"),
                    // From several threads at once: a late, smaller count must not move the page backwards.
                    Progress = p => _fsltlProgress = _fsltlProgress is { } last && last.Stage == p.Stage && last.Done > p.Done ? last : p,
                    Cancel = cancel,
                });
                foreach (var problem in result.Problems.Take(MaxProblemsLogged)) PacketConsole.Info($"fsltl problem: {problem}");
                if (result.Problems.Count > MaxProblemsLogged) PacketConsole.Info($"fsltl: {result.Problems.Count - MaxProblemsLogged} more problems not shown");
                PacketConsole.Info($"fsltl: imported {result.Liveries} liveries ({result.Models} models, {result.Skipped} skipped, {result.Problems.Count} problems)");
                _fsltlMessage = result.Liveries == 0 ? "CONVERT FAILED: SEE LOG" : $"{result.Liveries} LIVERIES CONVERTED";
                _fsltlConverting = false;
                _ = RefreshFolders();
                // The server read the catalogue when it started; it must read the new one for BATC's aircraft to be
                // drawn with these liveries. Not while BeyondATC is connected: a restart would drop it.
                if (result.Liveries > 0 && !State().BatcSeen)
                {
                    await RestartAsync().ConfigureAwait(false);
                }
                else if (result.Liveries > 0)
                {
                    PacketConsole.Info("fsltl: restart ClearanceLink (and BeyondATC) to draw traffic with the new models");
                    _fsltlMessage = "RESTART TO USE MODELS";
                }
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                PacketConsole.Info("fsltl: conversion stopped; the models converted before are kept");
                _fsltlMessage = "CONVERT STOPPED";
            }
            catch (Exception ex)
            {
                // Anything, not only I/O: an unexpected failure (a texture the decoder cannot read, out of memory)
                // must be seen on the page and in the log, not vanish with the background task.
                PacketConsole.Info($"fsltl: import failed: {ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}");
                _fsltlMessage = "CONVERT FAILED: SEE LOG";
            }
            finally
            {
                _fsltlConverting = false;
                _fsltlProgress = null;
                _ = RefreshFolders();
            }
        });
        return "CONVERTING FSLTL MODELS";
    }

    /// <summary>How many of a conversion's problems go in the log (the rest are counted).</summary>
    private const int MaxProblemsLogged = 25;

    /// <summary>What was last found on disk; looked up in the background at most every 10 s.</summary>
    private sealed record Found
    {
        public string? Batc { get; init; }
        public string? XPlane { get; init; }
        public PluginInstall.State AddOn { get; init; }
        public int Csl { get; init; }
        public string? Fsltl { get; init; }
        public int FsltlLiveries { get; init; }
        public bool FsltlOutOfDate { get; init; }
        public bool XPlaneRunning { get; init; }
        public bool MsfsInstalled { get; init; } = true;
        public BatcTrafficModels.Outcome? BatcModels { get; init; }
        public string? CslNames { get; init; }
        public string? BatcCommunity { get; init; }
        public Fsltl.FsltlHealth? FsltlHealth { get; init; }
        public DateTime At { get; init; }
    }

    private async Task StopAsync()
    {
        Task? serve;
        lock (_lock)
        {
            _stop?.Cancel();
            serve = _serve;
        }

        if (serve is not null)
        {
            try
            {
                await serve.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                PacketConsole.Info("panel: server did not stop within 10 s");
            }
        }

        lock (_lock)
        {
            _stop?.Dispose();
            _stop = null;
            _serve = null;
            _board = null;
        }
    }
}

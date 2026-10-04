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

    private readonly Lock _lock = new();
    private readonly DateTime _started = DateTime.Now;
    private CancellationTokenSource? _stop;
    private Task? _serve;
    private StatusBoard? _board;
    private Found _folders = new();
    private bool _foldersChanged;
    private volatile bool _fsltlConverting;

    public void Start()
    {
        lock (_lock)
        {
            _stop = new CancellationTokenSource();
            _board = null;
            var stop = _stop.Token;
            _serve = Task.Run(() => Cli.RunServerAsync("serve", Options.Parse(["--quiet"]), board => _board = board, stop));
        }
    }

    public async Task RestartAsync()
    {
        await StopAsync().ConfigureAwait(false);
        Start();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    public PanelState State()
    {
        var settings = AppSettings.Load(AppFolders.Default.Settings);
        var (batc, xplane) = Folders();
        var addOn = _folders.AddOn;
        var snapshot = _board?.Snapshot();
        var names = snapshot?.Clients.Select(c => c.Name).ToList() ?? [];
        var airport = snapshot is { Airports.Count: > 0 } ? snapshot.Airports[0] : null;
        var log = SessionLog.Path;
        return new PanelState(
            Cli.Version,
            Serving: snapshot is not null,
            XPlaneConnected: snapshot?.XPlane.Connected ?? false,
            XPlaneVersion: snapshot?.XPlane.Version,
            BatcHelper: names.Contains(StatusBoard.HelperClient),
            BatcApp: names.Contains(StatusBoard.AppClient),
            OtherAddOns: names.Count(n => n is not (StatusBoard.HelperClient or StatusBoard.AppClient or "")),
            LastAirport: airport?.Icao,
            LastAirportSource: airport?.Source,
            MsfsRunning: snapshot?.MsfsRunning ?? false,
            settings.Connection,
            OldConfigFiles: batc is not null && new BatcRedirect(batc).IsOn,
            settings.SessionLog,
            SessionLogBytes: log is not null && File.Exists(log) ? new FileInfo(log).Length : 0,
            settings.KeepDays,
            DateTime.Now - _started,
            batc,
            xplane,
            addOn,
            snapshot?.Plugin?.Aircraft,
            _folders.Csl,
            _folders.Fsltl,
            _folders.FsltlLiveries,
            _fsltlConverting,
            UpdateCheck.Available);
    }

    /// <summary>Carries out a key; returns a short line for the scratchpad, or null.</summary>
    public async Task<string?> DoAsync(CduKey key)
    {
        switch (key)
        {
            case CduKey.ConnectionAutomatic:
            case CduKey.ConnectionAlwaysOn:
                return await SetConnectionAsync(key == CduKey.ConnectionAutomatic ? ConnectionMode.Automatic : ConnectionMode.AlwaysOn).ConfigureAwait(false);
            case CduKey.RemoveOldFiles:
                if (Folders().Batc is { } folder)
                {
                    new BatcRedirect(folder).SwitchOff();
                }

                return "OLD FILES REMOVED";
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
            case CduKey.ConvertFsltl:
                return ConvertFsltl();
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

            settings.FsltlFolder = Path.GetFullPath(folder);
            settings.Save(AppFolders.Default.Settings);
            _folders = new();
            return "FSLTL FOLDER SAVED";
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
        _folders = new();
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
        if (Cli.ApplyConnection(settings, Folders().Batc) is not null)
        {
            return "SET BEYONDATC FOLDER";
        }

        settings.Save(AppFolders.Default.Settings);
        var batcWasConnected = State() is { BatcHelper: true } or { BatcApp: true };
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

    /// <summary>Saves that the getting-started pages are done; restarts the server when a folder was chosen meanwhile.</summary>
    public async Task<string> FinishGettingStartedAsync()
    {
        var settings = AppSettings.Load(AppFolders.Default.Settings);
        settings.GettingStartedDone = true;
        settings.Save(AppFolders.Default.Settings);
        if (_foldersChanged && State() is { BatcHelper: false, BatcApp: false })
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
    public static int CountCslPackages(string xplaneRoot, string? except = null) => CslFolders
        .Select(f => Path.Combine([xplaneRoot, .. f.Split('\\')]))
        .Where(Directory.Exists)
        .Sum(f => CountPackages(f, depth: 3, except is null ? null : Path.GetFullPath(except)));

    private static int CountPackages(string folder, int depth, string? except)
    {
        if (except is not null && string.Equals(Path.GetFullPath(folder), except, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (File.Exists(Path.Combine(folder, "xsb_aircraft.txt")))
        {
            return 1;
        }

        try
        {
            return depth == 0 ? 0 : Directory.GetDirectories(folder).Sum(d => CountPackages(d, depth - 1, except));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private string InstallTrafficAddOn()
    {
        if (Folders().XPlane is not { } xplane)
        {
            return "SET X-PLANE FOLDER";
        }

        // X-Plane keeps the plugin file open while it runs.
        if (_board?.Snapshot().XPlane.Connected == true)
        {
            return "CLOSE X-PLANE FIRST";
        }

        try
        {
            new PluginInstall(xplane).Install().ToList().ForEach(l => PacketConsole.Info($"plugin: {l}"));
            _folders = new();
            return "PLUGIN INSTALLED";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PacketConsole.Info($"plugin: could not install: {ex.Message}");
            return "CLOSE X-PLANE FIRST";
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

    /// <summary>The BeyondATC and X-Plane folders, looked up at most every 10 s (the registry search is slow).</summary>
    private (string? Batc, string? XPlane) Folders()
    {
        if (DateTime.Now - _folders.At > FolderRefresh)
        {
            var none = new Options();
            var xplane = Cli.FindXPlane(none)?.Root;
            _folders = new Found
            {
                Batc = Cli.BatcFolder(none),
                XPlane = xplane,
                AddOn = xplane is null ? PluginInstall.State.NotBundled : new PluginInstall(xplane).Check(),
                Csl = xplane is null ? 0 : CountCslPackages(xplane, except: Cli.FsltlOutput(xplane)),
                Fsltl = Cli.FsltlFolder(none),
                FsltlLiveries = xplane is null ? 0 : Fsltl.FsltlImporter.LoadCatalogue(Cli.FsltlOutput(xplane)).Count,
                At = DateTime.Now,
            };
        }

        return (_folders.Batc, _folders.XPlane);
    }

    /// <summary>Converts the user's FSLTL models into X-Plane's CSL folder in the background (it takes minutes).</summary>
    private string ConvertFsltl()
    {
        Folders();
        if (_folders.Fsltl is not { } package || _folders.XPlane is not { } xplane)
        {
            return _folders.XPlane is null ? "SET X-PLANE FOLDER" : "CHOOSE THE FSLTL FOLDER";
        }

        // Replacing files X-Plane has open can leave it drawing a mix of old and new models.
        if (Process.GetProcessesByName("X-Plane").Length > 0)
        {
            return "CLOSE X-PLANE FIRST";
        }

        if (_fsltlConverting)
        {
            return "ALREADY CONVERTING";
        }

        _fsltlConverting = true;
        _ = Task.Run(() =>
        {
            try
            {
                var result = Fsltl.FsltlImporter.Import(new Fsltl.ImportOptions
                {
                    PackageRoot = package,
                    Output = Cli.FsltlOutput(xplane, moveOld: true),
                    Log = line => PacketConsole.Info($"fsltl: {line}"),
                });
                PacketConsole.Info($"fsltl: imported {result.Liveries} liveries ({result.Models} models, {result.Skipped} skipped)");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                PacketConsole.Info($"fsltl: import failed: {ex.Message}");
            }
            finally
            {
                _fsltlConverting = false;
                _folders = new();
            }
        });
        return "CONVERTING FSLTL MODELS";
    }

    /// <summary>What was last found on disk; looked up at most every 10 s.</summary>
    private sealed record Found
    {
        public string? Batc { get; init; }
        public string? XPlane { get; init; }
        public PluginInstall.State AddOn { get; init; }
        public int Csl { get; init; }
        public string? Fsltl { get; init; }
        public int FsltlLiveries { get; init; }
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

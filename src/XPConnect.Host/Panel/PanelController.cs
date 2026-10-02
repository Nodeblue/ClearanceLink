using System.Diagnostics;
using XPConnect.Host.Status;
using XPConnect.Host.Support;
using XPConnect.Host.Transport;

namespace XPConnect.Host.Panel;

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
    private (string? Batc, string? XPlane, DateTime At) _folders;

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
            xplane);
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
        _folders = default;
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
            _folders = (Cli.BatcFolder(none), Cli.FindXPlane(none)?.Root, DateTime.Now);
        }

        return (_folders.Batc, _folders.XPlane);
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

using System.Runtime.InteropServices;
using XPConnect.Airports;
using XPConnect.Fsltl;
using XPConnect.Host;
using XPConnect.Host.Status;
using XPConnect.Host.Support;
using XPConnect.Host.Traffic;
using XPConnect.Host.Transport;
using XPConnect.Protocol;
using XPConnect.Recorder;
using XPConnect.Server;
using XPConnect.XPlane;

namespace XPConnect.Host
{
    public static class Program
    {
        /// <summary>
        /// No arguments (double-clicked) on Windows, or "panel": the XPConnect window. Anything else: the command line.
        /// STA because the window needs it on Windows.
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            if ((args.Length == 0 && OperatingSystem.IsWindows()) || args is ["panel"])
            {
                return Panel.PanelApp.Run();
            }

            Panel.ConsoleAttach.ToParent();
            return Cli.RunAsync(args).GetAwaiter().GetResult();
        }
    }

    public static class Cli
    {
        private const string Usage = """
            XPConnect - BeyondATC to X-Plane 12 bridge

            Usage:
              xpconnect
                  (no arguments, e.g. double-clicked) Open the XPConnect window (Windows), which serves while it is open.
              xpconnect panel
                  Open the XPConnect window on any system.
              xpconnect start
                  The same in this console window instead: check BeyondATC's set-up, then serve.
              xpconnect settings [connection automatic|always-on] [batc-folder <dir>|auto] [xplane-folder <dir>|auto] [session-log on|off] [keep-days <n>]
                  Show or change saved settings (%LOCALAPPDATA%\XPConnect\settings.json). connection automatic (default):
                  serve answers on MSFS's default connection while MSFS is closed, nothing changed in BeyondATC;
                  always-on: BeyondATC's SimConnect.cfg files point at XPConnect's TCP port. Folders set here are used
                  whenever --batc or --xplane-root is not given; "auto" goes back to finding them automatically.
              xpconnect support
                  Make a zip of the recent session logs, newest recording, settings, BeyondATC's Player.log and the end
                  of X-Plane's Log.txt (user name blanked out) to send for troubleshooting, and show it in Explorer.
              xpconnect plugin install|remove|status [--xplane-root <dir>]
                  Install the bundled X-Plane plugin that draws BATC's traffic (X-Plane must be closed), or remove it.
              xpconnect fsltl import|status [--fsltl <fsltl-traffic-base folder>] [--xplane-root <dir>] [--size 1024|2048|4096]
                    [--only <text>] [--force] [--out <dir>]
                  Convert your own FSLTL install (MSFS Community folder) into an X-Plane CSL package for the traffic
                  plugin, in <X-Plane>/Custom Data/CSL/XPConnect-FSLTL. Only reads FSLTL. --size caps textures (default
                  2048); --only imports liveries whose title or folder contains the text. Restart X-Plane afterwards.
              xpconnect batc on|off|status [--batc <BeyondATC folder>] [--port <n>]
                  Point BeyondATC at XPConnect (on) or back at MSFS (off) with SimConnect.cfg files in its folder.
              xpconnect serve [--listen <endpoint>] [--xplane <url>] [--mapping <dir>] [--xplane-root <dir>]
                    [--facilities <recording.jsonl>] [--stub <path> | --no-stub] [--no-traffic] [--status-port <port> | --no-status] [--open-status]
                    [--out <file.jsonl>] [--throttle-ms <ms>] [--quiet]
                  Stand in for MSFS 2024 and answer BATC from X-Plane. Without --listen: MSFS's default pipe while MSFS
                  is closed plus tcp:5112 (connection automatic), or tcp:5112 only (always-on). Defaults:
                  --xplane http://127.0.0.1:8086, --mapping ./mapping.
                  Airports come from the X-Plane folder (--xplane-root, else the one X-Plane's installer lists);
                  --facilities replays recorded MSFS airport answers first. Starts the FlightSimulator2024 stub
                  process BATC checks for, unless --no-stub. Status page on http://localhost:5577/ unless --no-status; --open-status opens it in the browser.
              xpconnect xplane-check [--xplane <url>] [--mapping <dir>] [--seconds <n>]
                  Connect to X-Plane, read every mapped dataref and print what BATC would be sent. Checks the mapping.
              xpconnect record --forward <endpoint> [--listen <endpoint>] [--out <file.jsonl>] [--throttle-ms <ms>] [--quiet]
                  Pass-through: BATC connects to --listen, packets go to the real sim at --forward. Records both ways.
              xpconnect fake [--listen <endpoint>] [--identity msfs2024|msfs2020] [--facilities <recording.jsonl>]
                    [--out <file.jsonl>] [--throttle-ms <ms>] [--quiet]
                  Pretend to be MSFS: answer the handshake and user-aircraft data requests, record every request.
                  --facilities replays the airport data MSFS sent in a pass-through recording.
              xpconnect facility-dump <file.jsonl> <ICAO> [--out <file.json>]
                  Decode the airport data answered for ICAO in a recording into readable JSON.
              xpconnect airport <ICAO> [--xplane-root <dir>] [--out <file.json>]
                  Build the airport answer XPConnect would send from X-Plane's files and write it as JSON.
              xpconnect summarize <file.jsonl> [--out <file.md>]
                  Write the call inventory (SimVars, events, facilities...) for a recording.

            Endpoints: pipe (default MSFS pipe), pipe:NAME, tcp:PORT, tcp:HOST:PORT
            --throttle-ms: record at most one packet per high-rate stream (AI position writes, SimObjectData) per
              interval; default 1000, 0 records everything. Forwarding is never throttled.
            """;

        public static async Task<int> RunAsync(string[] args)
        {
            if (args.Length == 0 || args is ["start"])
            {
                // Set BATC up if needed, then serve with the defaults in this console.
                return await StartAsync();
            }

            if (args[0] is "-h" or "--help" or "help")
            {
                Console.WriteLine(Usage);
                return 0;
            }

            if (args[0] is "--version" or "version")
            {
                Console.WriteLine($"XPConnect {Version}");
                return 0;
            }

            try
            {
                var options = Options.Parse(args.Skip(1));
                return args[0] switch
                {
                    "record" => await RunServerAsync("record", options),
                    "fake" => await RunServerAsync("fake", options),
                    "serve" => await RunServerAsync("serve", options),
                    "xplane-check" => await XPlaneCheckAsync(options),
                    "summarize" => Summarize(options),
                    "facility-dump" => DumpFacility(options),
                    "airport" => BuildAirport(options),
                    "batc" => Batc(options),
                    "settings" => SettingsCommand(options),
                    "support" => SupportCommand(),
                    "plugin" => Plugin(options),
                    "fsltl" => Fsltl(options),
                    _ => Fail($"Unknown command '{args[0]}'"),
                };
            }
            catch (FormatException ex)
            {
                return Fail(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Runs record, fake or serve until cancelled (Ctrl+C, or <paramref name="stop"/>). onBoard receives serve's
        /// status board once it exists (the window reads it).
        /// </summary>
        internal static async Task<int> RunServerAsync(string mode, Options options, Action<StatusBoard>? onBoard = null, CancellationToken stop = default)
        {
            PacketConsole.Verbose = !options.Has("quiet");
            StartSessionLog(mode);
            var listen = Endpoint.Parse(options.Get("listen") ?? (mode == "serve" ? "tcp:5112" : "pipe"));
            var outPath = options.Get("out")
                ?? Path.Combine(RecordingsFolder(), $"{DateTime.Now:yyyyMMdd-HHmmss}-{mode}.jsonl");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);

            // Ctrl+C, Ctrl+Break, closing the console window (SIGHUP on Windows) and kill all stop cleanly,
            // so the summary is always written.
            var signals = new[] { PosixSignal.SIGINT, PosixSignal.SIGQUIT, PosixSignal.SIGTERM, PosixSignal.SIGHUP }
                .Select(signal => PosixSignalRegistration.Create(signal, context =>
                {
                    context.Cancel = true;
                    cts.Cancel();
                }))
                .ToList();

            var inventory = new CallInventory();
            var throttleMs = int.Parse(options.Get("throttle-ms") ?? "1000", System.Globalization.CultureInfo.InvariantCulture);
            var throttle = new RecordThrottle(TimeSpan.FromMilliseconds(throttleMs));
            using var msfs = new MsfsGate(MsfsGate.RealMsfsRunning, PacketConsole.Info);
            using (var recorder = new JsonlRecorder(outPath, throttle: throttle))
            {
                recorder.Recorded += inventory.Add;
                await using var listener = mode == "serve" && options.Get("listen") is null
                    ? DefaultServeListener(msfs)
                    : Transports.Listen(listen);
                PacketConsole.Info($"Recording to {Path.GetFullPath(outPath)} (Ctrl+C to stop)");
                try
                {
                    if (mode == "record")
                    {
                        var forward = Endpoint.Parse(options.Get("forward")
                            ?? throw new ArgumentException("record needs --forward <endpoint> (the real simulator)"));
                        await new RecordingProxy(listener, forward, recorder).RunAsync(cts.Token);
                    }
                    else if (mode == "serve")
                    {
                        await ServeAsync(options, listener, recorder, msfs, onBoard, cts.Token);
                    }
                    else
                    {
                        var identity = ServerIdentity.FromName(options.Get("identity") ?? "msfs2024");
                        var facilities = LoadFacilities(options);

                        await new FakeSimServer(listener, identity, recorder, facilities: facilities).RunAsync(cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }

            inventory.SampledOut = throttle.Skipped;
            var summaryPath = Path.ChangeExtension(outPath, ".md");
            await File.WriteAllTextAsync(summaryPath, inventory.ToMarkdown($"Call inventory: {Path.GetFileName(outPath)}"), CancellationToken.None);
            PacketConsole.Info($"Stopped. {inventory.Count} packets. Summary: {Path.GetFullPath(summaryPath)}");
            signals.ForEach(s => s.Dispose());
            SessionLog.Stop();
            return 0;
        }

        /// <summary>Opens this session's log file (unless turned off in settings) and clears out old logs and recordings.</summary>
        private static void StartSessionLog(string mode)
        {
            var settings = AppSettings.Load(AppFolders.Default.Settings);
            if (settings.SessionLog)
            {
                SessionLog.Start(AppFolders.Default.Logs, mode, Version, DateTime.Now);
            }

            var pruned = SessionLog.Prune(AppFolders.Default.Logs, "*.log", settings.KeepDays, DateTime.Now)
                + SessionLog.Prune(AppFolders.Default.Recordings, "*.jsonl", settings.KeepDays, DateTime.Now)
                + SessionLog.Prune(AppFolders.Default.Recordings, "*.md", settings.KeepDays, DateTime.Now)
                + SessionLog.Prune(AppFolders.Default.SupportFiles, "*.zip", settings.KeepDays, DateTime.Now);
            PacketConsole.Info($"session log: {SessionLog.Path ?? "off"}" + (pruned > 0 ? $"; removed {pruned} files older than {settings.KeepDays} days" : ""));
        }

        /// <summary>--batc when given, else the folder saved in settings when it still holds BeyondATC, else found automatically.</summary>
        internal static string? BatcFolder(Options options)
        {
            if (options.Get("batc") is { } given)
            {
                return BatcRedirect.FindInstallFolder(given);
            }

            var saved = AppSettings.Load(AppFolders.Default.Settings).BatcFolder;
            return (saved is null ? null : BatcRedirect.FindInstallFolder(saved)) ?? BatcRedirect.FindInstallFolder();
        }

        /// <summary>--xplane-root when given, else the folder saved in settings when it is still X-Plane, else found automatically.</summary>
        internal static XPlaneInstall? FindXPlane(Options options)
        {
            if (options.Get("xplane-root") is { } given)
            {
                return XPlaneInstall.Find(given);
            }

            var saved = AppSettings.Load(AppFolders.Default.Settings).XPlaneFolder;
            return (saved is null ? null : XPlaneInstall.Find(saved)) ?? XPlaneInstall.Find();
        }

        private static int SettingsCommand(Options options)
        {
            var file = AppFolders.Default.Settings;
            var settings = AppSettings.Load(file);
            var args = options.Positional;
            if (args.Count % 2 != 0)
            {
                return Fail($"settings {args[^1]} needs a value");
            }

            for (var i = 0; i < args.Count; i += 2)
            {
                var (key, value) = (args[i], args[i + 1]);
                var auto = value.Equals("auto", StringComparison.OrdinalIgnoreCase);
                switch (key)
                {
                    case "batc-folder":
                        if (!auto && BatcRedirect.FindInstallFolder(value) is null)
                        {
                            return Fail($"BeyondATC.exe not found in {value}");
                        }

                        settings.BatcFolder = auto ? null : BatcRedirect.FindInstallFolder(value);
                        break;
                    case "xplane-folder":
                        if (!auto && XPlaneInstall.Find(value) is null)
                        {
                            return Fail($"{value} does not look like an X-Plane 12 folder (no Resources or Custom Scenery)");
                        }

                        settings.XPlaneFolder = auto ? null : Path.GetFullPath(value);
                        break;
                    case "connection":
                        settings.Connection = value switch
                        {
                            "automatic" => ConnectionMode.Automatic,
                            "always-on" => ConnectionMode.AlwaysOn,
                            _ => throw new ArgumentException("connection takes automatic or always-on"),
                        };
                        if (ApplyConnection(settings, BatcFolder(options)) is { } problem)
                        {
                            return Fail(problem);
                        }

                        break;
                    case "session-log":
                        settings.SessionLog = value switch
                        {
                            "on" => true,
                            "off" => false,
                            _ => throw new ArgumentException("session-log takes on or off"),
                        };
                        break;
                    case "keep-days":
                        settings.KeepDays = int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var days) && days > 0
                            ? days
                            : throw new ArgumentException("keep-days takes a whole number of days, 1 or more");
                        break;
                    default:
                        return Fail($"Unknown setting '{key}'. Use connection, batc-folder, xplane-folder, session-log or keep-days.");
                }
            }

            if (args.Count > 0)
            {
                settings.Save(file);
            }

            var none = new Options();
            Console.WriteLine($"Settings: {file}");
            Console.WriteLine($"  BeyondATC folder  {BatcFolder(none) ?? "not found"}{(settings.BatcFolder is null ? " (found automatically)" : "")}");
            Console.WriteLine($"  X-Plane folder    {FindXPlane(none)?.Root ?? "not found"}{(settings.XPlaneFolder is null ? " (found automatically)" : "")}");
            Console.WriteLine($"  Connection        {(settings.Connection == ConnectionMode.Automatic ? "automatic (MSFS's default connection; close MSFS before flying X-Plane)" : "always on (BeyondATC's config files point at XPConnect)")}");
            Console.WriteLine($"  Session log       {(settings.SessionLog ? "on" : "off")} ({AppFolders.Default.Logs})");
            Console.WriteLine($"  Keep logs for     {settings.KeepDays} days");
            return 0;
        }

        /// <summary>
        /// Always on writes BeyondATC's two config files; automatic removes XPConnect's (a user's own are put back).
        /// Returns a problem to show, or null.
        /// </summary>
        internal static string? ApplyConnection(AppSettings settings, string? batcFolder)
        {
            if (batcFolder is null)
            {
                return settings.Connection == ConnectionMode.AlwaysOn
                    ? "BeyondATC not found; set its folder first: settings batc-folder \"<BeyondATC folder>\""
                    : null;
            }

            var redirect = new BatcRedirect(batcFolder);
            var done = settings.Connection == ConnectionMode.AlwaysOn ? redirect.SwitchOn() : redirect.SwitchOff();
            done.ToList().ForEach(l => Console.WriteLine("  " + l));
            return null;
        }

        private static int SupportCommand()
        {
            var zip = CreateSupportFile();
            Console.WriteLine($"Support file: {zip}");
            Console.WriteLine("Your Windows user name has been blanked out. Please send this file to Nodeblue with a few words on what went wrong.");
            ShowInExplorer(zip);
            return 0;
        }

        /// <summary>Builds the support zip from the current settings and returns its path.</summary>
        internal static string CreateSupportFile()
        {
            var none = new Options();
            var sources = new SupportSources(Version, AppFolders.Default, BatcFolder(none), FindXPlane(none)?.Root,
                SupportSources.DefaultBatcLogFolder, Redactor.ForCurrentUser());
            return SupportBundle.Create(sources, DateTime.Now);
        }

        /// <summary>Opens Explorer with the file selected (or the folder shown); does nothing elsewhere.</summary>
        internal static void ShowInExplorer(string path)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                var args = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";
                System.Diagnostics.Process.Start("explorer.exe", args)?.Dispose();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The caller shows the path too.
            }
        }

        /// <summary>
        /// Where serve listens when --listen is not given. Automatic: MSFS's default pipe while MSFS is closed, plus
        /// XPConnect's TCP port for a BeyondATC still set up with config files. Always on: the TCP port only.
        /// </summary>
        private static IListener DefaultServeListener(MsfsGate msfs)
        {
            var tcp = Endpoint.Parse($"tcp:{BatcRedirect.DefaultPort}");
            if (AppSettings.Load(AppFolders.Default.Settings).Connection == ConnectionMode.AlwaysOn)
            {
                return Transports.Listen(tcp);
            }

            var listeners = new List<IListener> { new GatedPipeListener(Endpoint.Parse("pipe"), msfs) };
            try
            {
                listeners.Add(Transports.Listen(tcp));
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                PacketConsole.Info($"listen: {tcp} not available ({ex.Message}); default connection only");
            }

            return new MultiListener(listeners, PacketConsole.Info);
        }

        private static async Task ServeAsync(Options options, IListener listener, JsonlRecorder recorder, MsfsGate msfs, Action<StatusBoard>? onBoard, CancellationToken ct)
        {
            var mappings = LoadMappings(options);
            using var xplane = new XPlaneWebApiClient(XPlaneUri(options), PacketConsole.Info);
            var ground = new AirportGround();
            var traffic = new TrafficRegistry();
            var liveries = LoadLiveries(options);
            var facilities = ServeFacilities(options, ground);
            using var stub = new StubLauncher();
            var stubLine = "not started (--no-stub)";
            msfs.Check();
            if (!options.Has("no-stub"))
            {
                stubLine = msfs.IsRunning ? "stub: not started while MSFS runs" : stub.Start(options.Get("stub"));
                PacketConsole.Info(stubLine);

                // Step aside for the real MSFS: its process name is the stand-in's.
                msfs.Changed += running => PacketConsole.Info(running ? stub.Stop() : stub.Restart());
            }

            var msfsTask = msfs.RunAsync(ct);
            var xplaneTask = xplane.RunAsync(ct);
            await using var plugin = StartPluginLink();

            // BATC's traffic goes to the X-Plane plugin, and the plugin's terrain answers BATC's ground questions.
            var bridge = plugin is null || liveries is null ? null : new TrafficBridge(traffic, plugin, LoadTrafficModels(options), ground, PacketConsole.Info);
            var bridgeTask = bridge?.RunAsync(ct) ?? Task.CompletedTask;
            var server = new XPlaneSimServer(listener, ServerIdentity.Msfs2024, recorder, new SimVarResolver(mappings, xplane), xplane, facilities, traffic, (IGroundElevation?)bridge ?? ground, liveries);
            var board = new StatusBoard(() => server.Sessions, () => new XPlaneStatus(xplane.IsConnected, xplane.XPlaneVersion, XPlaneUri(options).ToString()), listener.Description)
            {
                MsfsRunning = () => msfs.IsRunning,
                Stub = stubLine.StartsWith("stub: ", StringComparison.Ordinal) ? stubLine["stub: ".Length..] : stubLine,
                Recording = Path.GetFullPath(recorder.Path),
                Traffic = () => traffic.Objects,
            };
            board.Plugin = () => plugin?.Status;
            onBoard?.Invoke(board);
            await using var status = new StatusServer(board, int.Parse(options.Get("status-port") ?? $"{StatusServer.DefaultPort}", System.Globalization.CultureInfo.InvariantCulture), PacketConsole.Info);
            if (!options.Has("no-status") && status.Start(ct) && options.Has("open-status"))
            {
                OpenInBrowser(status.Url);
            }

            try
            {
                await server.RunAsync(ct);
            }
            finally
            {
                await xplaneTask;
                await msfsTask;
                await bridgeTask;
            }
        }

        private static async Task<int> XPlaneCheckAsync(Options options)
        {
            var mappings = LoadMappings(options);
            using var xplane = new XPlaneWebApiClient(XPlaneUri(options), Console.WriteLine);
            var resolver = new SimVarResolver(mappings, xplane);
            var probes = mappings.SimVars
                .SelectMany(m => m.Indexed ? [$"{m.Name}:1", $"{m.Name}:2"] : new[] { m.Name })
                .ToList();
            probes.ForEach(p => resolver.Prepare(p));

            using var cts = new CancellationTokenSource();
            var run = xplane.RunAsync(cts.Token);
            var seconds = int.Parse(options.Get("seconds") ?? "5", System.Globalization.CultureInfo.InvariantCulture);
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            if (!xplane.IsConnected)
            {
                await cts.CancelAsync();
                await run;
                Console.WriteLine($"Could not reach X-Plane at {XPlaneUri(options)}. Is X-Plane 12 running with the Web API enabled?");
                return 1;
            }

            Console.WriteLine($"X-Plane {xplane.XPlaneVersion}. SimVar values as BATC would get them (units of the mapping source):");
            foreach (var probe in probes)
            {
                var map = mappings.FindSimVar(probe, out _)!;
                var (value, status) = resolver.Resolve(probe, map.Units);
                var shown = value.Text ?? value.Number.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);
                var flags = (map.Verify ? " VERIFY" : "") + (map.Status != "implemented" ? $" {map.Status}" : "");
                Console.WriteLine($"  {status,-7} {probe,-40} {shown,16} {map.Units}{flags}");
            }

            var missing = xplane.Missing;
            Console.WriteLine(missing.Count == 0 ? "All datarefs found." : $"NOT FOUND in X-Plane: {string.Join(", ", missing)}");
            await cts.CancelAsync();
            await run;
            return missing.Count == 0 ? 0 : 3;
        }

        internal static string Version =>
            typeof(Cli).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "dev";

        private static async Task<int> StartAsync()
        {
            Console.WriteLine($"XPConnect {Version} - BeyondATC for X-Plane 12");
            Console.WriteLine();
            if (BatcFolder(new Options()) is not { } folder)
            {
                Console.WriteLine("BeyondATC was not found (looked for BeyondATC.exe in its install folder and in C:\\BeyondATC).");
                Console.WriteLine("If it is installed somewhere else, run once:  XPConnect settings batc-folder \"<BeyondATC folder>\"");
                Console.WriteLine();
            }
            else
            {
                var settings = AppSettings.Load(AppFolders.Default.Settings);
                var redirect = new BatcRedirect(folder);
                if (settings.Connection == ConnectionMode.AlwaysOn && !redirect.IsOn)
                {
                    redirect.SwitchOn().ToList().ForEach(l => Console.WriteLine("  " + l));
                    Console.WriteLine();
                }
                else if (settings.Connection == ConnectionMode.Automatic && redirect.IsOn)
                {
                    // Set up by an older XPConnect: BeyondATC keeps working through the TCP port, but can't reach MSFS
                    // while XPConnect is closed.
                    Console.WriteLine("BeyondATC still has config files from an older XPConnect, which stop it working with MSFS.");
                    Console.WriteLine("XPConnect no longer needs them. Remove them? [Y/n]");
                    var answer = Console.IsInputRedirected ? "n" : Console.ReadLine();
                    if (answer is null || answer.Trim().Length == 0 || answer.Trim().StartsWith('y') || answer.Trim().StartsWith('Y'))
                    {
                        redirect.SwitchOff().ToList().ForEach(l => Console.WriteLine("  " + l));
                    }

                    Console.WriteLine();
                }
            }

            if (MsfsGate.RealMsfsRunning())
            {
                Console.WriteLine("Microsoft Flight Simulator is running. Close it so BeyondATC can connect to X-Plane.");
                Console.WriteLine();
            }

            OfferPluginInstall(new Options());

            Console.WriteLine("Start X-Plane 12 and load your aircraft, then start BeyondATC. Leave this window open while you fly;");
            Console.WriteLine("close it (or press Ctrl+C) when you are done. The status page opens in your browser.");
            Console.WriteLine();
            return await RunServerAsync("serve", Options.Parse(["--quiet", "--open-status"]));
        }

        internal static void OpenInBrowser(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
            {
                PacketConsole.Info($"status: could not open a browser ({ex.Message}); open {url} yourself");
            }
        }

        /// <summary>The link to the X-Plane traffic plugin; null when its port is taken (another XPConnect running).</summary>
        private static PluginLink? StartPluginLink()
        {
            try
            {
                var link = new PluginLink(Version, PacketConsole.Info);
                link.Start();
                return link;
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                PacketConsole.Info($"plugin: cannot listen on 127.0.0.1:{PluginLink.XPConnectPort} ({ex.Message}); traffic will not be drawn");
                return null;
            }
        }

        /// <summary>Double-click start: install or update the traffic plugin when this package carries one.</summary>
        private static void OfferPluginInstall(Options options)
        {
            if (FindXPlane(options) is not { } xplane)
            {
                return;
            }

            var plugin = new PluginInstall(xplane.Root);
            var state = plugin.Check();
            if (state is PluginInstall.State.NotBundled or PluginInstall.State.Installed)
            {
                return;
            }

            Console.WriteLine(state == PluginInstall.State.NotInstalled
                ? $"Install the XPConnect plugin into X-Plane ({xplane.Root}) so BeyondATC's traffic appears? [Y/n]"
                : "A newer XPConnect plugin is included. Update the one in X-Plane? [Y/n]");
            var answer = Console.IsInputRedirected ? "y" : Console.ReadLine();
            if (answer is not null && answer.Trim().Length > 0 && !answer.Trim().StartsWith('y') && !answer.Trim().StartsWith('Y'))
            {
                Console.WriteLine();
                return;
            }

            try
            {
                plugin.Install().ToList().ForEach(l => Console.WriteLine("  " + l));
                Console.WriteLine("  Traffic needs CSL aircraft models (for example Bluebell); see README.txt.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"  Could not install the plugin ({ex.Message}). Close X-Plane, then start XPConnect again.");
            }

            Console.WriteLine();
        }

        private static int Fsltl(Options options)
        {
            var action = options.Positional.FirstOrDefault() ?? "status";
            if (FindXPlane(options) is not { } xplane)
            {
                return Fail("X-Plane folder not found; pass --xplane-root <dir>");
            }

            var output = options.Get("out") ?? Path.Combine(xplane.Root, "Custom Data", "CSL", "XPConnect-FSLTL");
            var package = FsltlLocator.Find(options.Get("fsltl"));
            switch (action)
            {
                case "status":
                    Console.WriteLine(package is null ? "FSLTL: not found (pass --fsltl <folder>)" : $"FSLTL: {package}");
                    var catalogue = FsltlImporter.LoadCatalogue(output);
                    Console.WriteLine(catalogue.Count == 0 ? $"Imported: none ({output})" : $"Imported: {catalogue.Count} liveries in {output}");
                    return 0;
                case "import":
                    if (package is null)
                    {
                        return Fail(options.Get("fsltl") is { } given
                            ? $"No FSLTL package (SimObjects/Airplanes) in {given}"
                            : "FSLTL not found in the MSFS Community folder; pass --fsltl <fsltl-traffic-base folder>");
                    }

                    if (System.Diagnostics.Process.GetProcessesByName("X-Plane").Length > 0)
                    {
                        // Replacing objects and textures X-Plane has open can leave it drawing a mix of old and new files.
                        return Fail("Close X-Plane before importing; it reads the CSL files while it runs");
                    }

                    var only = options.Get("only");
                    var result = FsltlImporter.Import(new ImportOptions
                    {
                        PackageRoot = package,
                        Output = output,
                        MaxTextureSize = int.Parse(options.Get("size") ?? "2048", System.Globalization.CultureInfo.InvariantCulture),
                        Force = options.Has("force"),
                        Filter = only is null ? null : l => l.Title.Contains(only, StringComparison.OrdinalIgnoreCase)
                            || Path.GetFileName(l.Folder).Contains(only, StringComparison.OrdinalIgnoreCase),
                        Log = Console.WriteLine,
                    });
                    foreach (var problem in result.Problems) Console.WriteLine($"FSLTL problem: {problem}");
                    Console.WriteLine($"FSLTL: imported {result.Liveries} liveries ({result.Models} models, {result.Textures} textures, {result.Skipped} skipped) into {output}");
                    Console.WriteLine("Restart X-Plane (or reload plugins) so the traffic plugin loads them.");
                    return result.Liveries > 0 ? 0 : 1;
                default:
                    return Fail($"Unknown fsltl action '{action}' (import, status)");
            }
        }

        private static int Plugin(Options options)
        {
            var action = options.Positional.FirstOrDefault() ?? "status";
            if (FindXPlane(options) is not { } xplane)
            {
                return Fail("X-Plane folder not found; pass --xplane-root <dir>");
            }

            var plugin = new PluginInstall(xplane.Root);
            try
            {
                switch (action)
                {
                    case "install":
                        plugin.Install().ToList().ForEach(Console.WriteLine);
                        return plugin.Check() == PluginInstall.State.Installed ? 0 : 1;
                    case "remove":
                        plugin.Remove().ToList().ForEach(Console.WriteLine);
                        return 0;
                    case "status":
                        Console.WriteLine($"{plugin.Check()}: {plugin.Target} (bundled: {plugin.Bundled})");
                        return 0;
                    default:
                        return Fail($"Unknown plugin action '{action}' (install, remove, status)");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Fail($"{ex.Message} Close X-Plane first.");
            }
        }

        /// <summary>recordings/local in a source checkout; otherwise %LOCALAPPDATA%\XPConnect\recordings.</summary>
        private static string RecordingsFolder() =>
            Directory.Exists("recordings") ? Path.Combine("recordings", "local") : AppFolders.Default.Recordings;
        private static int Batc(Options options)
        {
            var action = options.Positional.FirstOrDefault() ?? "status";
            var folder = BatcFolder(options);
            if (folder is null)
            {
                return Fail(options.Get("batc") is { } given
                    ? $"BeyondATC.exe not found in {given}"
                    : "BeyondATC not found; pass --batc \"<BeyondATC folder>\"");
            }

            var port = int.Parse(options.Get("port") ?? BatcRedirect.DefaultPort.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
            var redirect = new BatcRedirect(folder, port);
            switch (action)
            {
                case "on":
                    redirect.SwitchOn().ToList().ForEach(Console.WriteLine);
                    Console.WriteLine("BeyondATC now connects to XPConnect (X-Plane). Run XPConnect before starting BeyondATC.");
                    return 0;
                case "off":
                    redirect.SwitchOff().ToList().ForEach(Console.WriteLine);
                    Console.WriteLine("BeyondATC now connects to MSFS again.");
                    return 0;
                case "status":
                    Console.WriteLine($"BeyondATC: {folder}");
                    foreach (var file in redirect.Files)
                    {
                        Console.WriteLine($"  {redirect.StateOf(file),-9} {file}");
                    }

                    Console.WriteLine(redirect.IsOn ? "Set up for X-Plane (XPConnect)." : "Set up for MSFS (or partly set up; run 'batc on' or 'batc off').");
                    return 0;
                default:
                    return Fail($"Unknown batc action '{action}'. Use on, off or status.");
            }
        }

        /// <summary>
        /// The model list sent for EnumerateSimObjectsAndLiveries: mapping/liveries.json, the titles and liveries MSFS
        /// listed in the 2026-09-30 recording. BATC picks its traffic models from it. Null (no traffic) with --no-traffic.
        /// </summary>
        private static IReadOnlyList<(string Title, string Livery)>? LoadLiveries(Options options)
        {
            if (options.Has("no-traffic"))
            {
                PacketConsole.Info("Traffic: off (--no-traffic); BeyondATC will not create AI aircraft");
                return null;
            }

            var dir = options.Get("mapping") ?? FindMappingDirectory();
            var path = dir is null ? null : Path.Combine(dir, "liveries.json");
            if (path is null || !File.Exists(path))
            {
                PacketConsole.Info("Traffic: mapping/liveries.json not found; BeyondATC will not create AI aircraft");
                return null;
            }

            var list = System.Text.Json.JsonSerializer.Deserialize<string[][]>(File.ReadAllText(path)) ?? [];
            PacketConsole.Info($"Traffic: {list.Length} models offered to BeyondATC from {Path.GetFullPath(path)}");
            return [.. list.Where(e => e.Length >= 1).Select(e => (e[0], e.Length > 1 ? e[1] : ""))];
        }
        /// <summary>mapping/traffic-models.yaml: how BATC's model titles become ICAO types and airlines for the plugin.</summary>
        private static TrafficModels LoadTrafficModels(Options options)
        {
            var dir = options.Get("mapping") ?? FindMappingDirectory();
            var path = dir is null ? null : Path.Combine(dir, "traffic-models.yaml");
            if (path is null || !File.Exists(path))
            {
                PacketConsole.Info("Traffic: mapping/traffic-models.yaml not found; every aircraft is drawn as the default model");
                return TrafficModels.Empty;
            }

            var models = TrafficModels.Load(path);
            if (FindXPlane(options) is { } xplane)
            {
                var imported = Path.Combine(xplane.Root, "Custom Data", "CSL", "XPConnect-FSLTL");
                models.UseImported(FsltlImporter.LoadCatalogue(imported).Select(e => (e.Title, e.Key, e.IcaoType, e.IcaoAirline)));
                if (models.ImportedCount > 0) PacketConsole.Info($"Traffic: {models.ImportedCount} imported FSLTL liveries from {imported}");
            }

            return models;
        }

        private static MappingSet LoadMappings(Options options)
        {
            var dir = options.Get("mapping") ?? FindMappingDirectory()
                ?? throw new ArgumentException("mapping directory not found; pass --mapping <dir>");
            var mappings = MappingSet.LoadDirectory(dir);
            PacketConsole.Info($"Mapping: {mappings.SimVars.Count} SimVars, {mappings.Events.Count} events from {Path.GetFullPath(dir)}");
            return mappings;
        }

        private static string? FindMappingDirectory()
        {
            foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            {
                for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                {
                    var candidate = Path.Combine(dir.FullName, "mapping");
                    if (File.Exists(Path.Combine(candidate, "simvars.yaml")))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        // 127.0.0.1, not localhost: X-Plane listens on IPv4 only, and on Windows each request to localhost first
        // tries ::1 and waits about two seconds before falling back.
        private static Uri XPlaneUri(Options options) => new(options.Get("xplane") ?? "http://127.0.0.1:8086/");

        private static FacilityReplay? LoadFacilities(Options options)
        {
            if (options.Get("facilities") is not { } source)
            {
                return null;
            }

            var facilities = FacilityReplay.FromRecording(source);
            PacketConsole.Info($"Facility replay: {facilities.Count} airport requests loaded from {source}");
            return facilities;
        }

        /// <summary>Recorded answers first (when --facilities is given), then airports built from X-Plane's files.</summary>
        private static FacilitySourceChain? ServeFacilities(Options options, AirportGround? ground = null)
        {
            var sources = new List<IFacilitySource>();
            if (LoadFacilities(options) is { } replay)
            {
                sources.Add(replay);
            }

            if (FindXPlane(options) is { } install)
            {
                var airports = new XPlaneAirports(install, PacketConsole.Info);
                airports.Start();
                sources.Add(new XPlaneFacilitySource(airports, ground));
                PacketConsole.Info($"Airports: from X-Plane at {install.Root}");
            }
            else
            {
                PacketConsole.Info("Airports: X-Plane folder not found (pass --xplane-root); airports BATC asks for will be unknown");
            }

            return sources.Count == 0 ? null : new FacilitySourceChain([.. sources]);
        }

        private static int BuildAirport(Options options)
        {
            var icao = options.Positional.FirstOrDefault() ?? throw new ArgumentException("airport needs an ICAO code");
            var install = FindXPlane(options)
                ?? throw new ArgumentException("X-Plane folder not found; pass --xplane-root <dir>");
            var airport = new XPlaneAirports(install, Console.Error.WriteLine).Find(icao);
            if (airport is null)
            {
                Console.Error.WriteLine($"{icao}: no scenery pack in {install.Root} defines it");
                return 2;
            }

            var text = FacilityDump.Serialize(FacilityDump.ToJson(airport));
            if (options.Get("out") is { } output)
            {
                File.WriteAllText(output, text);
                Console.Error.WriteLine($"Wrote {Path.GetFullPath(output)}");
            }
            else
            {
                Console.WriteLine(text);
            }

            return 0;
        }

        private static int DumpFacility(Options options)
        {
            if (options.Positional.Count < 2)
            {
                throw new ArgumentException("facility-dump needs <file.jsonl> <ICAO>");
            }

            var text = FacilityDump.Serialize(FacilityDump.FromRecording(options.Positional[0], options.Positional[1]));
            if (options.Get("out") is { } output)
            {
                File.WriteAllText(output, text);
            }
            else
            {
                Console.WriteLine(text);
            }

            return 0;
        }

        private static int Summarize(Options options)
        {
            var input = options.Positional.FirstOrDefault() ?? throw new ArgumentException("summarize needs a .jsonl file");
            var output = options.Get("out") ?? Path.ChangeExtension(input, ".md");
            var inventory = CallInventory.FromFile(input);
            File.WriteAllText(output, inventory.ToMarkdown($"Call inventory: {Path.GetFileName(input)}"));
            Console.WriteLine($"{inventory.Count} packets summarised to {output}");
            return 0;
        }

        private static int Fail(string message)
        {
            Console.Error.WriteLine(message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(Usage);
            return 2;
        }
    }

    public sealed class Options
    {
        private readonly Dictionary<string, string?> _named = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Positional { get; } = [];

        public static Options Parse(IEnumerable<string> args)
        {
            var options = new Options();
            using var e = args.GetEnumerator();
            while (e.MoveNext())
            {
                var arg = e.Current;
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    options.Positional.Add(arg);
                    continue;
                }

                var key = arg[2..];
                options._named[key] = key is "quiet" or "no-stub" or "no-status" or "open-status" or "force" ? null : e.MoveNext() ? e.Current : throw new FormatException($"{arg} needs a value");
            }

            return options;
        }

        public string? Get(string key) => _named.GetValueOrDefault(key);

        public bool Has(string key) => _named.ContainsKey(key);
    }
}

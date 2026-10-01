using System.Runtime.InteropServices;
using XPConnect.Host;
using XPConnect.Host.Transport;
using XPConnect.Protocol;
using XPConnect.Recorder;
using XPConnect.Server;
using XPConnect.XPlane;

return await Cli.RunAsync(args);

namespace XPConnect.Host
{
    public static class Cli
    {
        private const string Usage = """
            XPConnect - BeyondATC to X-Plane 12 bridge

            Usage:
              xpconnect serve [--listen <endpoint>] [--xplane <url>] [--mapping <dir>] [--facilities <recording.jsonl>]
                    [--stub <path> | --no-stub] [--out <file.jsonl>] [--throttle-ms <ms>] [--quiet]
                  Stand in for MSFS 2024 and answer BATC from X-Plane. Defaults: --listen tcp:5112 (what the
                  SimConnect.cfg redirect points at), --xplane http://127.0.0.1:8086, --mapping ./mapping.
                  Starts the FlightSimulator2024 stub process BATC checks for, unless --no-stub.
              xpconnect xplane-check [--xplane <url>] [--mapping <dir>] [--seconds <n>]
                  Connect to X-Plane, read every mapped dataref and print what BATC would be sent. Checks the mapping.
              xpconnect record --forward <endpoint> [--listen <endpoint>] [--out <file.jsonl>] [--throttle-ms <ms>] [--quiet]
                  Pass-through: BATC connects to --listen, packets go to the real sim at --forward. Records both ways.
              xpconnect fake [--listen <endpoint>] [--identity msfs2024|msfs2020] [--facilities <recording.jsonl>]
                    [--out <file.jsonl>] [--throttle-ms <ms>] [--quiet]
                  Pretend to be MSFS: answer the handshake and user-aircraft data requests, record every request.
                  --facilities replays the airport data MSFS sent in a pass-through recording.
              xpconnect summarize <file.jsonl> [--out <file.md>]
                  Write the call inventory (SimVars, events, facilities...) for a recording.

            Endpoints: pipe (default MSFS pipe), pipe:NAME, tcp:PORT, tcp:HOST:PORT
            --throttle-ms: record at most one packet per high-rate stream (AI position writes, SimObjectData) per
              interval; default 1000, 0 records everything. Forwarding is never throttled.
            """;

        public static async Task<int> RunAsync(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
            {
                Console.WriteLine(Usage);
                return args.Length == 0 ? 1 : 0;
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

        private static async Task<int> RunServerAsync(string mode, Options options)
        {
            PacketConsole.Verbose = !options.Has("quiet");
            var listen = Endpoint.Parse(options.Get("listen") ?? (mode == "serve" ? "tcp:5112" : "pipe"));
            var outPath = options.Get("out")
                ?? Path.Combine("recordings", "local", $"{DateTime.Now:yyyyMMdd-HHmmss}-{mode}.jsonl");

            using var cts = new CancellationTokenSource();

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
            using (var recorder = new JsonlRecorder(outPath, throttle: throttle))
            {
                recorder.Recorded += inventory.Add;
                await using var listener = Transports.Listen(listen);
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
                        await ServeAsync(options, listener, recorder, cts.Token);
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
            await File.WriteAllTextAsync(summaryPath, inventory.ToMarkdown($"Call inventory: {Path.GetFileName(outPath)}"));
            PacketConsole.Info($"Stopped. {inventory.Count} packets. Summary: {Path.GetFullPath(summaryPath)}");
            signals.ForEach(s => s.Dispose());
            return 0;
        }

        private static async Task ServeAsync(Options options, IListener listener, JsonlRecorder recorder, CancellationToken ct)
        {
            var mappings = LoadMappings(options);
            using var xplane = new XPlaneWebApiClient(XPlaneUri(options), PacketConsole.Info);
            var facilities = LoadFacilities(options);
            using var stub = new StubLauncher();
            if (!options.Has("no-stub"))
            {
                PacketConsole.Info(stub.Start(options.Get("stub")));
            }

            var xplaneTask = xplane.RunAsync(ct);
            var server = new XPlaneSimServer(listener, ServerIdentity.Msfs2024, recorder, new SimVarResolver(mappings, xplane), xplane, facilities);
            try
            {
                await server.RunAsync(ct);
            }
            finally
            {
                await xplaneTask;
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
                options._named[key] = key is "quiet" or "no-stub" ? null : e.MoveNext() ? e.Current : throw new FormatException($"{arg} needs a value");
            }

            return options;
        }

        public string? Get(string key) => _named.GetValueOrDefault(key);

        public bool Has(string key) => _named.ContainsKey(key);
    }
}

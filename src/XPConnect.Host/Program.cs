using System.Runtime.InteropServices;
using XPConnect.Host;
using XPConnect.Host.Transport;
using XPConnect.Protocol;
using XPConnect.Recorder;

return await Cli.RunAsync(args);

namespace XPConnect.Host
{
    public static class Cli
    {
        private const string Usage = """
            XPConnect - BeyondATC to X-Plane 12 bridge (Phase 0 tools)

            Usage:
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
            var listen = Endpoint.Parse(options.Get("listen") ?? "pipe");
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
                    else
                    {
                        var identity = ServerIdentity.FromName(options.Get("identity") ?? "msfs2024");
                        var facilities = options.Get("facilities") is { } source ? FacilityReplay.FromRecording(source) : null;
                        if (facilities is not null)
                        {
                            PacketConsole.Info($"Facility replay: {facilities.Count} airport requests loaded from {options.Get("facilities")}");
                        }

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
                options._named[key] = key == "quiet" ? null : e.MoveNext() ? e.Current : throw new FormatException($"{arg} needs a value");
            }

            return options;
        }

        public string? Get(string key) => _named.GetValueOrDefault(key);

        public bool Has(string key) => _named.ContainsKey(key);
    }
}

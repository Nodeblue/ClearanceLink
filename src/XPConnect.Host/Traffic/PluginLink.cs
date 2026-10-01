using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace XPConnect.Host.Traffic;

/// <summary>State of one traffic aircraft as the X-Plane plugin draws it (docs/protocol/plugin.md, <c>pos</c>).</summary>
public readonly record struct TrafficState(
    double Latitude,
    double Longitude,
    double AltitudeFeet,
    double HeadingTrue,
    double Pitch,
    double Bank,
    double GroundSpeedKnots,
    bool OnGround,
    double Gear,
    double Flaps,
    double Spoilers,
    double Thrust,
    TrafficLights Lights);

[Flags]
public enum TrafficLights
{
    None = 0,
    Nav = 1,
    Beacon = 2,
    Strobe = 4,
    Landing = 8,
    Taxi = 16,
}

/// <summary>What the plugin last reported about itself.</summary>
public sealed record PluginStatus(string Version, int Aircraft, int Models, IReadOnlyList<string> CslFolders, DateTimeOffset At);

/// <summary>
/// XPConnect's side of the X-Plane plugin link: sends BATC's traffic to the plugin over local UDP and keeps what the
/// plugin reports back (its status, terrain height under each aircraft, errors). Thread-safe. Protocol:
/// docs/protocol/plugin.md.
/// </summary>
public sealed class PluginLink : IPluginTraffic, IAsyncDisposable
{
    public const int PluginPort = 49710;
    public const int XPConnectPort = 49711;

    /// <summary>The plugin counts as connected while its status lines keep arriving.</summary>
    public static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(5);

    private readonly UdpClient _udp;
    private readonly IPEndPoint _plugin;
    private readonly string _version;
    private readonly Action<string> _log;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<uint, double> _groundFeet = new();
    private readonly ConcurrentDictionary<uint, string> _errors = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _receive;
    private Task? _hello;
    private PluginStatus? _status;

    public PluginLink(string version, Action<string> log, int listenPort = XPConnectPort, int pluginPort = PluginPort, TimeProvider? time = null)
    {
        _version = version;
        _log = log;
        _time = time ?? TimeProvider.System;
        _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, listenPort));
        if (OperatingSystem.IsWindows())
        {
            // A send to the plugin's port while X-Plane is closed must not break later receives (WSAECONNRESET).
            const int SioUdpConnReset = -1744830452;
            _udp.Client.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
        }

        _plugin = new IPEndPoint(IPAddress.Loopback, pluginPort);
    }

    public int ListenPort => ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;

    /// <summary>The plugin's last status, or null if it has not reported within <see cref="StatusTimeout"/>.</summary>
    public PluginStatus? Status => _status is { } s && _time.GetUtcNow() - s.At <= StatusTimeout ? s : null;

    public event Action<uint, string>? AircraftError;

    public void Start()
    {
        _receive = Task.Run(ReceiveAsync);
        _hello = Task.Run(HelloAsync);
    }

    /// <summary>Terrain elevation (feet MSL) the plugin last reported under this aircraft.</summary>
    public bool TryGetGroundFeet(uint id, out double feet) => _groundFeet.TryGetValue(id, out feet);

    public void Add(uint id, string icaoType, string? icaoAirline, string? livery, string label) =>
        Send($"add {id} {Word(icaoType)} {Word(icaoAirline)} {Word(livery)} {OneLine(label)}".TrimEnd());

    public void Update(uint id, TrafficState state) => Send(FormatPos(id, state));

    public void Remove(uint id)
    {
        _groundFeet.TryRemove(id, out _);
        _errors.TryRemove(id, out _);
        Send($"del {id}");
    }

    public void Clear()
    {
        _groundFeet.Clear();
        Send("clear");
    }

    public static string FormatPos(uint id, TrafficState s) => string.Create(
        CultureInfo.InvariantCulture,
        $"pos {id} {s.Latitude:0.0000000} {s.Longitude:0.0000000} {s.AltitudeFeet:0.0} {s.HeadingTrue:0.00} {s.Pitch:0.00} {s.Bank:0.00} {s.GroundSpeedKnots:0.0} {(s.OnGround ? 1 : 0)} {Ratio(s.Gear)} {Ratio(s.Flaps)} {Ratio(s.Spoilers)} {Ratio(s.Thrust)} {(int)s.Lights}");

    /// <summary>Applies one line from the plugin. Public for tests.</summary>
    public void Handle(string line)
    {
        var parts = line.Split(' ', 5, StringSplitOptions.RemoveEmptyEntries);
        switch (parts)
        {
            case ["ground", var id, var feet, ..] when uint.TryParse(id, CultureInfo.InvariantCulture, out var aircraft)
                && double.TryParse(feet, NumberStyles.Float, CultureInfo.InvariantCulture, out var elevation):
                _groundFeet[aircraft] = elevation;
                break;
            case ["status", var version, var aircraftCount, var models, ..]:
                var folders = line.Split(' ', 5) is { Length: 5 } all && all[4] != "-" ? all[4].Split(';') : [];
                var previous = _status;
                _status = new PluginStatus(version, Int(aircraftCount), Int(models), folders, _time.GetUtcNow());
                if (previous is null || previous.Models != _status.Models)
                {
                    _log($"plugin: X-Plane plugin {version} connected, {_status.Models} CSL models from {folders.Length} folders");
                }

                break;
            case ["error", var id, ..] when uint.TryParse(id, CultureInfo.InvariantCulture, out var aircraft):
                var message = line.Split(' ', 3) is { Length: 3 } e ? e[2] : "";
                if (_errors.TryAdd(aircraft, message))
                {
                    _log($"plugin: aircraft {aircraft} not drawn: {message}");
                }

                AircraftError?.Invoke(aircraft, message);
                break;
        }
    }

    private void Send(string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        try
        {
            _udp.Send(bytes, bytes.Length, _plugin);
        }
        catch (SocketException)
        {
            // X-Plane is not running; the plugin gets the next update when it is.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task ReceiveAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udp.ReceiveAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            foreach (var line in Encoding.UTF8.GetString(result.Buffer).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Handle(line);
            }
        }
    }

    private async Task HelloAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), _time);
        try
        {
            do
            {
                Send($"hello {_version}");
            }
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string Word(string? text) => string.IsNullOrWhiteSpace(text) ? "-" : text.Trim().Replace(' ', '_');

    private static string OneLine(string text) => text.ReplaceLineEndings(" ").Trim();

    private static string Ratio(double value) => Math.Clamp(value, 0, 1).ToString("0.###", CultureInfo.InvariantCulture);

    private static int Int(string text) => int.TryParse(text, CultureInfo.InvariantCulture, out var n) ? n : 0;

    public async ValueTask DisposeAsync()
    {
        Send("clear");
        await _stop.CancelAsync().ConfigureAwait(false);
        _udp.Dispose();
        foreach (var task in new[] { _receive, _hello })
        {
            if (task is not null)
            {
                await task.ConfigureAwait(false);
            }
        }

        _stop.Dispose();
    }
}

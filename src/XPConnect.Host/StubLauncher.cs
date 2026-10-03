using System.Diagnostics;

namespace XPConnect.Host;

/// <summary>
/// Starts the do-nothing FlightSimulator2024 process BATC looks for before it connects (see
/// docs/research/batc-call-inventory.md), unless one is already running, and stops it again on exit.
/// </summary>
public sealed class StubLauncher : IDisposable
{
    public const string ProcessName = "FlightSimulator2024";

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> StartedIds = new();

    private Process? _started;
    private string? _explicitPath;

    /// <summary>True when the process is XPConnect's stand-in: one this XPConnect started, or the stub file next to it.</summary>
    public static bool IsStandIn(Process process)
    {
        if (StartedIds.ContainsKey(process.Id))
        {
            return true;
        }

        try
        {
            var path = process.MainModule?.FileName;
            return path is not null && Path.GetDirectoryName(path) is { } dir
                && string.Equals(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // Can't read its path (exited, or another user's process): treat it as the real sim.
            return false;
        }
    }

    public static string? FindExecutable(string? explicitPath)
    {
        if (explicitPath is not null)
        {
            return File.Exists(explicitPath) ? Path.GetFullPath(explicitPath) : null;
        }

        var name = OperatingSystem.IsWindows() ? ProcessName + ".exe" : ProcessName;
        var candidate = Path.Combine(AppContext.BaseDirectory, name);
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>Returns a line for the log saying what happened.</summary>
    public string Start(string? explicitPath)
    {
        _explicitPath = explicitPath;
        var running = Process.GetProcessesByName(ProcessName);
        if (running.Length > 0)
        {
            var pid = running[0].Id;
            Array.ForEach(running, p => p.Dispose());
            return $"stub: {ProcessName} already running (pid {pid}); if that is the real MSFS, close it first";
        }

        if (FindExecutable(explicitPath) is not { } path)
        {
            return $"stub: {ProcessName} executable not found next to XPConnect; BATC will say the sim is not running (use --stub <path>)";
        }

        _started = Process.Start(new ProcessStartInfo(path, ["--parent", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)])
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Minimized,
        });
        if (_started is not null)
        {
            StartedIds[_started.Id] = true;
        }

        return $"stub: started {path} (pid {_started?.Id})";
    }

    /// <summary>Starts the stand-in again after <see cref="Stop"/> (MSFS closed again).</summary>
    public string Restart() => _started is null ? Start(_explicitPath) : "stub: already running";

    /// <summary>Stops the stand-in so it can't be mistaken for MSFS while the real one runs.</summary>
    public string Stop()
    {
        if (_started is null)
        {
            return "stub: not running";
        }

        var pid = _started.Id;
        if (!_started.HasExited)
        {
            _started.Kill();
        }

        _started.Dispose();
        _started = null;
        StartedIds.TryRemove(pid, out _);
        return $"stub: stopped (pid {pid}) while MSFS runs";
    }

    public void Dispose() => Stop();
}

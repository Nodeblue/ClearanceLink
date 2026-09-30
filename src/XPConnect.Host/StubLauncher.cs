using System.Diagnostics;

namespace XPConnect.Host;

/// <summary>
/// Starts the do-nothing FlightSimulator2024 process BATC looks for before it connects (see
/// docs/research/batc-call-inventory.md), unless one is already running, and stops it again on exit.
/// </summary>
public sealed class StubLauncher : IDisposable
{
    public const string ProcessName = "FlightSimulator2024";

    private Process? _started;

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

        _started = Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Minimized,
        });
        return $"stub: started {path} (pid {_started?.Id})";
    }

    public void Dispose()
    {
        if (_started is { HasExited: false })
        {
            _started.Kill();
        }

        _started?.Dispose();
    }
}

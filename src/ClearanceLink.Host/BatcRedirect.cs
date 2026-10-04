using Microsoft.Win32;

namespace ClearanceLink.Host;

/// <summary>
/// Points BeyondATC at ClearanceLink, or back at MSFS. BATC opens two SimConnect connections: its helper
/// simconnect_ws.exe (BeyondATC_Data\StreamingAssets) and BeyondATC.exe itself. Each reads a SimConnect.cfg next to
/// it, so a cfg naming ClearanceLink's TCP port in both folders redirects BATC without touching MSFS
/// (docs/research/batc-call-inventory.md). Removing them sends BATC back to MSFS's default pipe.
/// </summary>
public sealed class BatcRedirect(string installFolder, int port = BatcRedirect.DefaultPort)
{
    public const int DefaultPort = 5112;

    private const string FileName = "SimConnect.cfg";
    private const string BackupName = "SimConnect.cfg.clearancelink-backup";

    public enum FileState
    {
        /// <summary>No SimConnect.cfg: BATC connects to MSFS.</summary>
        Missing,

        /// <summary>A SimConnect.cfg pointing at ClearanceLink.</summary>
        ClearanceLink,

        /// <summary>Some other SimConnect.cfg (the user's own); left alone except for a backup when switching on.</summary>
        Other,
    }

    public string InstallFolder { get; } = installFolder;

    /// <summary>The two places BATC reads a SimConnect.cfg from.</summary>
    public IReadOnlyList<string> Files =>
    [
        Path.Combine(InstallFolder, FileName),
        Path.Combine(InstallFolder, "BeyondATC_Data", "StreamingAssets", FileName),
    ];

    public string Content => $"[SimConnect]\r\nProtocol=IPv4\r\nAddress=127.0.0.1\r\nPort={port}\r\n";

    /// <summary>
    /// BeyondATC's folder: the one given, else its uninstall entry's install location, else the usual places.
    /// Null when none of them holds BeyondATC.exe.
    /// </summary>
    public static string? FindInstallFolder(string? explicitFolder = null)
    {
        var candidates = new List<string?> { explicitFolder };
        if (explicitFolder is null)
        {
            candidates.AddRange(UninstallLocations());
            candidates.Add(@"C:\BeyondATC");
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BeyondATC"));
        }

        return candidates
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!.Trim().TrimEnd('\\', '/'))
            .FirstOrDefault(c => File.Exists(Path.Combine(c, "BeyondATC.exe")));
    }

    public FileState StateOf(string file)
    {
        if (!File.Exists(file))
        {
            return FileState.Missing;
        }

        var keys = File.ReadAllLines(file)
            .Select(l => l.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()[1], StringComparer.OrdinalIgnoreCase);
        var ours = string.Equals(keys.GetValueOrDefault("Protocol"), "IPv4", StringComparison.OrdinalIgnoreCase)
            && keys.GetValueOrDefault("Address") == "127.0.0.1"
            && keys.GetValueOrDefault("Port") == port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ours ? FileState.ClearanceLink : FileState.Other;
    }

    /// <summary>True when both files point at ClearanceLink.</summary>
    public bool IsOn => Files.All(f => StateOf(f) == FileState.ClearanceLink);

    /// <summary>Writes both files. Another SimConnect.cfg already there is kept as SimConnect.cfg.clearancelink-backup.</summary>
    public IReadOnlyList<string> SwitchOn()
    {
        var done = new List<string>();
        foreach (var file in Files)
        {
            switch (StateOf(file))
            {
                case FileState.ClearanceLink:
                    done.Add($"already set: {file}");
                    continue;
                case FileState.Other:
                    var backup = Path.Combine(Path.GetDirectoryName(file)!, BackupName);
                    if (!File.Exists(backup))
                    {
                        File.Move(file, backup);
                        done.Add($"kept your existing file as {backup}");
                    }

                    break;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, Content);
            done.Add($"wrote {file}");
        }

        return done;
    }

    /// <summary>Removes ClearanceLink's files and puts back any file that was there before.</summary>
    public IReadOnlyList<string> SwitchOff()
    {
        var done = new List<string>();
        foreach (var file in Files)
        {
            if (StateOf(file) == FileState.ClearanceLink)
            {
                File.Delete(file);
                done.Add($"removed {file}");
            }
            else if (File.Exists(file))
            {
                done.Add($"left alone (not ClearanceLink's): {file}");
            }

            foreach (var name in new[] { BackupName, Support.OldNames.CfgBackup })
            {
                var backup = Path.Combine(Path.GetDirectoryName(file)!, name);
                if (File.Exists(backup) && !File.Exists(file))
                {
                    File.Move(backup, file);
                    done.Add($"restored {file}");
                }
            }
        }

        return done;
    }

    private static IEnumerable<string?> UninstallLocations()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        const string uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var root = RegistryKey.OpenBaseKey(hive == Registry.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, view)
                    .OpenSubKey(uninstall);
                if (root is null)
                {
                    continue;
                }

                foreach (var name in root.GetSubKeyNames())
                {
                    using var app = root.OpenSubKey(name);
                    if (app?.GetValue("DisplayName") is string display && display.StartsWith("BeyondATC", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return app.GetValue("InstallLocation") as string;
                    }
                }
            }
        }
    }
}

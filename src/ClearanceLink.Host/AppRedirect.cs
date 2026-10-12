namespace ClearanceLink.Host;

/// <summary>
/// Points the ATC app at ClearanceLink, or back at MSFS. A SimConnect client reads a SimConnect.cfg next to its .exe, so a
/// cfg naming ClearanceLink's TCP port there redirects that app only, without touching MSFS
/// (docs/research/atc-app-call-inventory.md). A Unity app may also run a helper from <c>&lt;Name&gt;_Data\StreamingAssets</c>
/// (the app's versions up to 1.8.21 did), so when that folder exists a cfg goes there too. Removing them sends the app
/// back to MSFS's default pipe.
/// </summary>
public sealed class AppRedirect(AtcApp app, int port = AppRedirect.DefaultPort)
{
    public const int DefaultPort = 5112;

    private const string FileName = "SimConnect.cfg";
    private const string BackupName = "SimConnect.cfg.clearancelink-backup";

    public enum FileState
    {
        /// <summary>No SimConnect.cfg: the app connects to MSFS.</summary>
        Missing,

        /// <summary>A SimConnect.cfg pointing at ClearanceLink.</summary>
        ClearanceLink,

        /// <summary>Some other SimConnect.cfg (the user's own); left alone except for a backup when switching on.</summary>
        Other,
    }

    public AtcApp App { get; } = app;

    /// <summary>The places the app reads a SimConnect.cfg from: next to its .exe, and its StreamingAssets folder when it has one.</summary>
    public IReadOnlyList<string> Files
    {
        get
        {
            var files = new List<string> { Path.Combine(App.Folder, FileName) };
            if (App.DataFolder is { } data && Directory.Exists(Path.Combine(data, "StreamingAssets")))
            {
                files.Add(Path.Combine(data, "StreamingAssets", FileName));
            }

            return files;
        }
    }

    public string Content => $"[SimConnect]\r\nProtocol=IPv4\r\nAddress=127.0.0.1\r\nPort={port}\r\n";

    public FileState StateOf(string file)
    {
        if (!File.Exists(file))
        {
            return FileState.Missing;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(file);
        }
        catch (IOException)
        {
            // Being written right now (always on rewrites it while the window reads it): treat as not there yet.
            return FileState.Missing;
        }

        var keys = lines
            .Select(l => l.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()[1], StringComparer.OrdinalIgnoreCase);
        var ours = string.Equals(keys.GetValueOrDefault("Protocol"), "IPv4", StringComparison.OrdinalIgnoreCase)
            && keys.GetValueOrDefault("Address") == "127.0.0.1"
            && keys.GetValueOrDefault("Port") == port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ours ? FileState.ClearanceLink : FileState.Other;
    }

    /// <summary>True when every file points at ClearanceLink.</summary>
    public bool IsOn => Files.All(f => StateOf(f) == FileState.ClearanceLink);

    /// <summary>True when ClearanceLink can create files in the app's folders (not a protected folder).</summary>
    public bool CanWrite()
    {
        foreach (var file in Files)
        {
            var probe = Path.Combine(Path.GetDirectoryName(file)!, "ClearanceLink.write-test");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(probe, "");
                File.Delete(probe);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Writes the files. Another SimConnect.cfg already there is kept as SimConnect.cfg.clearancelink-backup.
    /// Throws <see cref="RedirectFailedException"/> when a file cannot be written (a read-only or protected folder).
    /// </summary>
    public IReadOnlyList<string> SwitchOn()
    {
        var done = new List<string>();
        foreach (var file in Files)
        {
            try
            {
                SwitchOn(file, done);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new RedirectFailedException(
                    $"could not write {file}: run ClearanceLink as administrator, or use connection automatic ({ex.Message})", file, ex);
            }
        }

        return done;
    }

    /// <summary>
    /// Removes ClearanceLink's files and puts back any file that was there before.
    /// Throws <see cref="RedirectFailedException"/> when a file cannot be removed or restored.
    /// </summary>
    public IReadOnlyList<string> SwitchOff()
    {
        var done = new List<string>();
        foreach (var file in Files)
        {
            try
            {
                SwitchOff(file, done);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new RedirectFailedException(
                    $"could not remove {file}: run ClearanceLink as administrator, or delete it yourself ({ex.Message})", file, ex);
            }
        }

        return done;
    }

    private void SwitchOn(string file, List<string> done)
    {
        switch (StateOf(file))
        {
            case FileState.ClearanceLink:
                done.Add($"already set: {file}");
                return;
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

    private void SwitchOff(string file, List<string> done)
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
}

/// <summary>The app's SimConnect.cfg could not be written or removed (a read-only or protected folder).</summary>
public sealed class RedirectFailedException(string message, string file, Exception inner) : IOException(message, inner)
{
    public string FilePath { get; } = file;
}

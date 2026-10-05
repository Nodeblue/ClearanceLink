namespace ClearanceLink.Host.Support;

/// <summary>
/// Copies ClearanceLink's status lines (not every packet: those go to the recording) to one file per session in the
/// logs folder, so a user can send them in a support file after closing the window.
/// </summary>
public static class SessionLog
{
    private static readonly Lock Gate = new();
    private static StreamWriter? _writer;

    public static string? Path { get; private set; }

    /// <summary>Opens logs\yyyyMMdd-HHmmss-&lt;mode&gt;.log. Never throws: a log that cannot be opened is skipped.</summary>
    public static void Start(string folder, string mode, string version, DateTime now)
    {
        lock (Gate)
        {
            if (_writer is not null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(folder);
                Path = System.IO.Path.Combine(folder, $"{now:yyyyMMdd-HHmmss}-{mode}.log");
                _writer = new StreamWriter(new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                _writer.WriteLine($"{now:yyyy-MM-dd HH:mm:ss.fff} ** session: ClearanceLink {version} {mode}, {Environment.OSVersion}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Path = null;
                _writer = null;
            }
        }
    }

    public static void Write(string line)
    {
        lock (Gate)
        {
            try
            {
                _writer?.WriteLine(line);
            }
            catch (IOException)
            {
                // A full disk must not stop the server.
            }
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            _writer?.Dispose();
            _writer = null;
            Path = null;
        }
    }

    /// <summary>Most the recordings folder may hold; the oldest recordings go first when it is over.</summary>
    public const long MaxRecordingBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// Deletes the oldest files (by last write) matching any of the patterns until the rest total at most
    /// <paramref name="maxBytes"/>; the newest file is always kept. Files that cannot be deleted are skipped.
    /// Returns how many went.
    /// </summary>
    public static int PruneToSize(string folder, IReadOnlyList<string> patterns, long maxBytes)
    {
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        var files = patterns
            .SelectMany(p => new DirectoryInfo(folder).EnumerateFiles(p))
            .DistinctBy(f => f.FullName)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();
        var total = files.Sum(f => f.Length);
        var deleted = 0;
        for (var i = files.Count - 1; i > 0 && total > maxBytes; i--)
        {
            try
            {
                var length = files[i].Length;
                files[i].Delete();
                total -= length;
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use (another ClearanceLink recording) or read-only: leave it for next time.
            }
        }

        return deleted;
    }

    /// <summary>Deletes files matching the pattern older than keepDays (by last write). Returns how many went.</summary>
    public static int Prune(string folder, string pattern, int keepDays, DateTime now)
    {
        if (keepDays <= 0 || !Directory.Exists(folder))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(folder, pattern))
        {
            if (now - File.GetLastWriteTime(file) <= TimeSpan.FromDays(keepDays))
            {
                continue;
            }

            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use (another ClearanceLink running) or read-only: leave it for next time.
            }
        }

        return deleted;
    }
}

namespace XPConnect.Host.Support;

/// <summary>
/// Copies XPConnect's status lines (not every packet: those go to the recording) to one file per session in the
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
                _writer.WriteLine($"{now:yyyy-MM-dd HH:mm:ss.fff} ** session: XPConnect {version} {mode}, {Environment.OSVersion}");
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
                // In use (another XPConnect running) or read-only: leave it for next time.
            }
        }

        return deleted;
    }
}

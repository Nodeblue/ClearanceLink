using System.IO.Compression;
using XPConnect.Host.Support;

namespace XPConnect.Host.Tests;

public sealed class SupportTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("xpconnect-support").FullName;

    public void Dispose()
    {
        SessionLog.Stop();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Settings_round_trip_and_default_when_missing()
    {
        var file = Path.Combine(_root, "app", "settings.json");
        var defaults = AppSettings.Load(file);
        Assert.True(defaults.SessionLog);
        Assert.Equal(7, defaults.KeepDays);
        Assert.False(defaults.GettingStartedDone);
        Assert.Null(defaults.BatcFolder);

        new AppSettings { BatcFolder = @"D:\Games\BeyondATC", SessionLog = false, KeepDays = 3, GettingStartedDone = true }.Save(file);
        var loaded = AppSettings.Load(file);

        Assert.Equal(@"D:\Games\BeyondATC", loaded.BatcFolder);
        Assert.Null(loaded.XPlaneFolder);
        Assert.False(loaded.SessionLog);
        Assert.Equal(3, loaded.KeepDays);
        Assert.True(loaded.GettingStartedDone);
        Assert.False(File.Exists(file + ".tmp"));
    }

    [Fact]
    public void A_damaged_settings_file_is_kept_aside_and_defaults_are_used()
    {
        var file = Path.Combine(_root, "settings.json");
        File.WriteAllText(file, "{ not json");

        var loaded = AppSettings.Load(file);

        Assert.True(loaded.SessionLog);
        Assert.Equal("{ not json", File.ReadAllText(file + ".bad"));
    }

    [Fact]
    public void Redactor_blanks_the_profile_folder_and_the_user_name_as_a_word()
    {
        var redactor = new Redactor(@"C:\Users\Ben", "Ben");

        Assert.Equal(@"log at %USERPROFILE%\AppData\x", redactor.Apply(@"log at C:\Users\Ben\AppData\x"));
        Assert.Equal("%USERPROFILE%/Documents", redactor.Apply("c:/users/ben/Documents"));
        Assert.Equal(@"""C:\\Users\\<user>\\Desktop""", redactor.Apply(@"""C:\\Users\\Ben\\Desktop"""));
        Assert.Equal("hello <user>, Bench and Benjamin stay", redactor.Apply("hello Ben, Bench and Benjamin stay"));
    }

    [Fact]
    public void Session_log_copies_status_lines_and_prune_removes_only_old_files()
    {
        var logs = Path.Combine(_root, "logs");
        SessionLog.Start(logs, "serve", "1.2.3", new DateTime(2026, 10, 2, 8, 0, 0));
        PacketConsole.Info("xplane: CONNECTED");
        var path = SessionLog.Path!;
        SessionLog.Stop();

        Assert.Equal("20261002-080000-serve.log", Path.GetFileName(path));
        var text = File.ReadAllText(path);
        Assert.Contains("XPConnect 1.2.3 serve", text, StringComparison.Ordinal);
        Assert.Contains("** xplane: CONNECTED", text, StringComparison.Ordinal);

        var old = Path.Combine(logs, "20260901-080000-serve.log");
        File.WriteAllText(old, "old");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-8));
        File.WriteAllText(Path.Combine(logs, "notes.txt"), "not a log");
        File.SetLastWriteTime(Path.Combine(logs, "notes.txt"), DateTime.Now.AddDays(-30));

        Assert.Equal(1, SessionLog.Prune(logs, "*.log", 7, DateTime.Now));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(Path.Combine(logs, "notes.txt")));
    }

    [Fact]
    public void Support_zip_holds_logs_recording_batc_and_xplane_files_with_the_user_blanked()
    {
        var folders = new AppFolders(Path.Combine(_root, "app"));
        new AppSettings { XPlaneFolder = @"C:\Users\Ben\X-Plane 12" }.Save(folders.Settings);
        Directory.CreateDirectory(folders.Logs);
        for (var i = 0; i < SupportBundle.LogsIncluded + 2; i++)
        {
            var log = Path.Combine(folders.Logs, $"2026100{i}-080000-serve.log");
            File.WriteAllText(log, $"log {i} from C:\\Users\\Ben\\Desktop");
            File.SetLastWriteTime(log, DateTime.Now.AddHours(-10 + i));
        }

        Directory.CreateDirectory(folders.Recordings);
        File.WriteAllText(Path.Combine(folders.Recordings, "old-serve.jsonl"), "{}");
        File.SetLastWriteTime(Path.Combine(folders.Recordings, "old-serve.jsonl"), DateTime.Now.AddHours(-5));
        File.WriteAllText(Path.Combine(folders.Recordings, "new-serve.jsonl"), "{\"name\":\"Open\"}");
        File.WriteAllText(Path.Combine(folders.Recordings, "new-serve.md"), "# summary");

        var batc = Path.Combine(_root, "BeyondATC");
        Directory.CreateDirectory(Path.Combine(batc, "BeyondATC_Data", "StreamingAssets"));
        File.WriteAllText(Path.Combine(batc, "BeyondATC.exe"), "");
        new BatcRedirect(batc).SwitchOn();
        var batcLogs = Path.Combine(_root, "LocalLow");
        Directory.CreateDirectory(batcLogs);
        File.WriteAllText(Path.Combine(batcLogs, "Player.log"), "Detected Microsoft Flight Simulator 2024 is in use");

        var xplane = Path.Combine(_root, "X-Plane 12");
        Directory.CreateDirectory(xplane);
        File.WriteAllText(Path.Combine(xplane, "Log.txt"), new string('x', 100) + "\nLoaded: Ben's Phenom\n");

        var zipPath = SupportBundle.Create(
            new SupportSources("1.2.3", folders, batc, xplane, batcLogs, new Redactor(@"C:\Users\Ben", "Ben")),
            new DateTime(2026, 10, 2, 9, 30, 0));

        Assert.Equal("XPConnect-support-20261002-093000.zip", Path.GetFileName(zipPath));
        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToHashSet();
        Assert.Contains("about.txt", names);
        Assert.Contains("settings.json", names);
        Assert.Equal(SupportBundle.LogsIncluded, names.Count(n => n.StartsWith("logs/", StringComparison.Ordinal)));
        Assert.DoesNotContain("logs/20261000-080000-serve.log", names);
        Assert.Contains("recording/new-serve.jsonl", names);
        Assert.Contains("recording/new-serve.md", names);
        Assert.DoesNotContain("recording/old-serve.jsonl", names);
        Assert.Contains("beyondatc/Player.log", names);
        Assert.Contains("beyondatc/SimConnect.cfg", names);
        Assert.Contains("beyondatc/StreamingAssets-SimConnect.cfg", names);
        Assert.Contains("xplane/Log.txt", names);

        var all = string.Join("\n", zip.Entries.Select(Read));
        Assert.DoesNotContain(@"Users\Ben", all, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<user>'s Phenom", all, StringComparison.Ordinal);
        Assert.Contains("XPConnect 1.2.3 support file", Read(zip.GetEntry("about.txt")!), StringComparison.Ordinal);
        Assert.Contains("XPConnect", Read(zip.GetEntry("about.txt")!), StringComparison.Ordinal);
    }

    private static string Read(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}

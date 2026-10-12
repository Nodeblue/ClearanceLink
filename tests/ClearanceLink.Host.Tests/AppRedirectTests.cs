using ClearanceLink.Host;
using ClearanceLink.Host.Support;

namespace ClearanceLink.Host.Tests;

public sealed class AppRedirectTests : IDisposable
{
    // A Unity app like the one recorded: Tower.exe with Tower_Data\StreamingAssets beside it.
    private readonly string _folder = Directory.CreateTempSubdirectory("clearancelink-app").FullName;
    private readonly AtcApp _app;

    public AppRedirectTests()
    {
        File.WriteAllText(Path.Combine(_folder, "Tower.exe"), "");
        Directory.CreateDirectory(Path.Combine(_folder, "Tower_Data", "StreamingAssets"));
        File.WriteAllText(Path.Combine(_folder, "Tower_Data", "app.info"), "Example Games, Inc\nTower");
        _app = AtcApp.From(Path.Combine(_folder, "Tower.exe"))!;
    }

    [Fact]
    public void The_app_is_an_existing_exe_and_its_unity_folders_come_from_app_info()
    {
        Assert.Null(AtcApp.From(null));
        Assert.Null(AtcApp.From(Path.Combine(_folder, "Missing.exe")));
        Assert.Null(AtcApp.From(Path.Combine(_folder, "Tower_Data", "app.info")));

        Assert.Equal(_folder, _app.Folder);
        Assert.Equal("Tower", _app.Name);
        Assert.Equal(Path.Combine(_folder, "Tower_Data"), _app.DataFolder);
        Assert.Equal(("Example Games, Inc", "Tower"), (_app.Company, _app.Product));
        Assert.EndsWith(Path.Combine("AppData", "LocalLow", "Example Games, Inc", "Tower", "Player.log"), _app.PlayerLog);
        Assert.Equal(@"Software\Example Games, Inc\Tower", _app.PrefsKey);
    }

    [Fact]
    public void An_app_that_is_not_a_unity_app_gets_one_file_and_no_log()
    {
        var other = Directory.CreateTempSubdirectory("clearancelink-other").FullName;
        try
        {
            File.WriteAllText(Path.Combine(other, "Radar.exe"), "");
            var app = AtcApp.From(Path.Combine(other, "Radar.exe"))!;

            Assert.Null(app.DataFolder);
            Assert.Null(app.PlayerLog);
            Assert.Null(app.PrefsKey);
            Assert.Equal([Path.Combine(other, "SimConnect.cfg")], new AppRedirect(app).Files);
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

    [Fact]
    public void The_exe_is_found_in_a_folder_saved_by_an_earlier_version()
    {
        File.WriteAllText(Path.Combine(_folder, "UnityCrashHandler64.exe"), "");

        // The .exe with a matching _Data folder wins over the others.
        Assert.Equal(Path.Combine(_folder, "Tower.exe"), AtcApp.FindExeIn(_folder));
        Assert.Null(AtcApp.FindExeIn(Path.Combine(_folder, "Tower_Data")));
        Assert.Null(AtcApp.FindExeIn(Path.Combine(_folder, "nowhere")));
    }

    [Fact]
    public void Settings_from_an_earlier_version_carry_the_folder_over_as_the_exe()
    {
        var file = Path.Combine(_folder, "settings.json");
        File.WriteAllText(file, $$"""{ "{{OldNames.AppFolderSetting}}": {{System.Text.Json.JsonSerializer.Serialize(_folder)}}, "connection": "always-on" }""");

        var settings = AppSettings.Load(file);
        Assert.Equal(Path.Combine(_folder, "Tower.exe"), settings.AppExe);
        Assert.Equal(ConnectionMode.AlwaysOn, settings.Connection);

        settings.Save(file);
        Assert.DoesNotContain(OldNames.AppFolderSetting, File.ReadAllText(file));
        Assert.Equal(Path.Combine(_folder, "Tower.exe"), AppSettings.Load(file).AppExe);
    }

    [Fact]
    public void Choosing_another_app_takes_the_files_from_the_old_one()
    {
        var other = Directory.CreateTempSubdirectory("clearancelink-other").FullName;
        try
        {
            File.WriteAllText(Path.Combine(other, "Radar.exe"), "");
            var settings = new AppSettings { Connection = ConnectionMode.AlwaysOn };
            Assert.Null(Cli.ChangeApp(settings, _app));
            Assert.True(new AppRedirect(_app).IsOn);

            var radar = AtcApp.From(Path.Combine(other, "Radar.exe"))!;
            Assert.Null(Cli.ChangeApp(settings, radar));
            Assert.Equal(radar.Exe, settings.AppExe);
            Assert.True(new AppRedirect(radar).IsOn);
            Assert.All(new AppRedirect(_app).Files, f => Assert.False(File.Exists(f)));
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

    [Fact]
    public void On_writes_both_files_and_off_removes_them()
    {
        var redirect = new AppRedirect(_app);
        Assert.False(redirect.IsOn);

        redirect.SwitchOn();

        Assert.True(redirect.IsOn);
        Assert.All(redirect.Files, f => Assert.Contains("Port=5112", File.ReadAllText(f)));

        redirect.SwitchOff();

        Assert.All(redirect.Files, f => Assert.False(File.Exists(f)));
        Assert.All(redirect.Files, f => Assert.Equal(AppRedirect.FileState.Missing, redirect.StateOf(f)));
    }

    [Fact]
    public void A_users_own_file_is_backed_up_and_restored()
    {
        var redirect = new AppRedirect(_app);
        var own = redirect.Files[1];
        File.WriteAllText(own, "[SimConnect]\nProtocol=IPv4\nAddress=192.168.1.20\nPort=500\n");
        Assert.Equal(AppRedirect.FileState.Other, redirect.StateOf(own));

        redirect.SwitchOn();
        Assert.True(redirect.IsOn);

        redirect.SwitchOff();
        Assert.Contains("192.168.1.20", File.ReadAllText(own));
        Assert.False(File.Exists(redirect.Files[0]));
    }

    [Fact]
    public void Recognises_the_hand_written_file_from_phase_0()
    {
        // The file written by hand on Ben's PC (2026-09-30), LF line ends, no trailing newline.
        var redirect = new AppRedirect(_app);
        File.WriteAllText(redirect.Files[0], "[SimConnect]\nProtocol=IPv4\nAddress=127.0.0.1\nPort=5112");

        Assert.Equal(AppRedirect.FileState.ClearanceLink, redirect.StateOf(redirect.Files[0]));
    }

    [Fact]
    public void A_file_that_cannot_be_written_is_a_plain_message_not_a_crash()
    {
        // A directory where the cfg should go stands in for a read-only folder (tests may run as root).
        var redirect = new AppRedirect(_app);
        Directory.CreateDirectory(redirect.Files[1]);

        var ex = Assert.Throws<RedirectFailedException>(() => redirect.SwitchOn());
        Assert.Equal(redirect.Files[1], ex.FilePath);
        Assert.StartsWith($"could not write {redirect.Files[1]}: run ClearanceLink as administrator, or use connection automatic", ex.Message);

        var problem = Cli.ApplyConnection(new AppSettings { Connection = ConnectionMode.AlwaysOn }, _app);
        Assert.NotNull(problem);
        Assert.StartsWith("could not write ", problem);
        Assert.Equal(Cli.AppNotChosenProblem, Cli.ApplyConnection(new AppSettings { Connection = ConnectionMode.AlwaysOn }, null));
    }

    [Fact]
    public void Can_write_is_false_for_a_folder_that_refuses_files()
    {
        var redirect = new AppRedirect(_app);
        Assert.True(redirect.CanWrite());
        Assert.All(Directory.GetFiles(_folder, "*", SearchOption.AllDirectories), f => Assert.True(f.EndsWith("Tower.exe", StringComparison.Ordinal) || f.EndsWith("app.info", StringComparison.Ordinal)));

        // A directory where the probe file goes stands in for a protected folder (tests may run as root).
        Directory.CreateDirectory(Path.Combine(_folder, "Tower_Data", "StreamingAssets", "ClearanceLink.write-test"));
        Assert.False(redirect.CanWrite());
    }

    [Fact]
    public void Link_points_the_app_at_ClearanceLink_only_while_MSFS_is_closed_and_undoes_it_on_exit()
    {
        var redirect = new AppRedirect(_app);
        var log = new List<string>();
        var guards = 0;
        var link = new AppLink(redirect, log.Add, () => guards++);

        link.Follow(msfsRunning: false);
        Assert.True(redirect.IsOn);
        Assert.Equal(1, guards);

        link.Follow(msfsRunning: true);
        Assert.All(redirect.Files, f => Assert.False(File.Exists(f)));

        link.Follow(msfsRunning: false);
        Assert.True(redirect.IsOn);
        Assert.Equal(1, guards);

        // Always on was chosen just before the restart: the files stay.
        link.Release(ConnectionMode.AlwaysOn);
        Assert.True(redirect.IsOn);

        link.Release(ConnectionMode.Automatic);
        Assert.All(redirect.Files, f => Assert.False(File.Exists(f)));
        Assert.Null(link.Problem);
        Assert.Contains(log, l => l.StartsWith("app: the ATC app pointed at ClearanceLink", StringComparison.Ordinal));
        Assert.Contains(log, l => l.StartsWith("app: ClearanceLink closing", StringComparison.Ordinal));
    }

    [Fact]
    public void Link_puts_back_a_users_own_file_on_exit()
    {
        var redirect = new AppRedirect(_app);
        const string own = "[SimConnect]\r\nProtocol=IPv4\r\nAddress=192.168.1.20\r\nPort=500\r\n";
        File.WriteAllText(redirect.Files[0], own);
        var link = new AppLink(redirect, _ => { });

        link.Follow(msfsRunning: false);
        Assert.True(redirect.IsOn);

        link.Release(ConnectionMode.Automatic);
        Assert.Equal(own, File.ReadAllText(redirect.Files[0]));
        Assert.False(File.Exists(redirect.Files[1]));
    }

    [Fact]
    public void Link_reports_a_folder_it_cannot_write_without_throwing()
    {
        var redirect = new AppRedirect(_app);
        Directory.CreateDirectory(redirect.Files[1]);
        var log = new List<string>();
        var link = new AppLink(redirect, log.Add);

        link.Follow(msfsRunning: false);

        Assert.StartsWith("could not write ", link.Problem);
        Assert.Contains(log, l => l.StartsWith("app: could not write ", StringComparison.Ordinal));
    }

    [Fact]
    public void Always_on_puts_back_files_an_app_update_removed()
    {
        // Issue #35: connection always on, both files gone, the ATC app fell back to MSFS's pipe and could not connect.
        var redirect = new AppRedirect(_app);
        redirect.SwitchOn();
        var log = new List<string>();
        var link = new AppLink(redirect, log.Add);

        link.KeepOn();
        Assert.Empty(log); // Already in place: nothing to say.

        redirect.Files.ToList().ForEach(File.Delete);
        link.KeepOn();
        Assert.True(redirect.IsOn);
        Assert.Contains(log, l => l.StartsWith("app: always on; the ATC app's config files were missing, written again", StringComparison.Ordinal));

        // Serve stopping in always on leaves them, and a late timer tick does nothing.
        link.Release(ConnectionMode.AlwaysOn);
        redirect.Files.ToList().ForEach(File.Delete);
        link.KeepOn();
        Assert.All(redirect.Files, f => Assert.False(File.Exists(f)));
    }

    [Fact]
    public void Always_on_keeps_trying_and_logs_a_problem_once()
    {
        var redirect = new AppRedirect(_app);
        Directory.CreateDirectory(redirect.Files[1]);
        var log = new List<string>();
        var link = new AppLink(redirect, log.Add);

        link.KeepOn();
        link.KeepOn();

        Assert.StartsWith("could not write ", link.Problem);
        Assert.Single(log, l => l.StartsWith("app: could not write ", StringComparison.Ordinal));

        Directory.Delete(redirect.Files[1]);
        link.KeepOn();
        Assert.Null(link.Problem);
        Assert.True(redirect.IsOn);
    }

    [Fact]
    public void Always_on_stops_once_the_command_line_switches_it_off()
    {
        var redirect = new AppRedirect(_app);
        var alwaysOn = false;
        var link = new AppLink(redirect, _ => { });

        link.KeepOnEvery(TimeSpan.FromMinutes(5), () => alwaysOn);
        Thread.Sleep(200);
        link.KeepOn();
        Assert.All(redirect.Files, f => Assert.False(File.Exists(f)));

        alwaysOn = true;
        link.KeepOn();
        link.Release(ConnectionMode.AlwaysOn);
        Assert.True(redirect.IsOn);
    }

    [Fact]
    public void Always_on_timer_writes_the_files_straight_away()
    {
        var redirect = new AppRedirect(_app);
        var link = new AppLink(redirect, _ => { });

        link.KeepOnEvery(TimeSpan.FromMinutes(5), () => true);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!redirect.IsOn && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }

        link.Release(ConnectionMode.AlwaysOn);
        Assert.True(redirect.IsOn);
    }

    [Theory]
    [InlineData(ConnectionMode.Automatic, false, true)]
    [InlineData(ConnectionMode.Automatic, true, false)]
    [InlineData(ConnectionMode.AlwaysOn, false, false)]
    public void Guard_removes_the_files_only_when_nothing_else_needs_them(ConnectionMode mode, bool portInUse, bool release)
    {
        Assert.Equal(release, AppLink.GuardShouldRelease(mode, portInUse));
    }

    [Fact]
    public void Port_in_use_sees_a_listener()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            Assert.True(Cli.PortInUse(((System.Net.IPEndPoint)listener.LocalEndpoint).Port));
        }
        finally
        {
            listener.Stop();
        }
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}

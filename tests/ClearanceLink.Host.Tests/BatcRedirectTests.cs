using ClearanceLink.Host;
using ClearanceLink.Host.Support;

namespace ClearanceLink.Host.Tests;

public sealed class BatcRedirectTests : IDisposable
{
    private readonly string _batc = Directory.CreateTempSubdirectory("clearancelink-batc").FullName;

    public BatcRedirectTests()
    {
        File.WriteAllText(Path.Combine(_batc, "BeyondATC.exe"), "");
        Directory.CreateDirectory(Path.Combine(_batc, "BeyondATC_Data", "StreamingAssets"));
    }

    [Fact]
    public void Finds_the_folder_given_only_when_it_holds_BeyondATC()
    {
        Assert.Equal(_batc, BatcRedirect.FindInstallFolder(_batc + "\\"));
        Assert.Null(BatcRedirect.FindInstallFolder(Path.Combine(_batc, "BeyondATC_Data")));
    }

    [Fact]
    public void On_writes_both_files_and_off_removes_them()
    {
        var redirect = new BatcRedirect(_batc);
        Assert.False(redirect.IsOn);

        redirect.SwitchOn();

        Assert.True(redirect.IsOn);
        Assert.All(redirect.Files, f => Assert.Contains("Port=5112", File.ReadAllText(f)));

        redirect.SwitchOff();

        Assert.All(redirect.Files, f => Assert.False(File.Exists(f)));
        Assert.All(redirect.Files, f => Assert.Equal(BatcRedirect.FileState.Missing, redirect.StateOf(f)));
    }

    [Fact]
    public void A_users_own_file_is_backed_up_and_restored()
    {
        var redirect = new BatcRedirect(_batc);
        var own = redirect.Files[1];
        File.WriteAllText(own, "[SimConnect]\nProtocol=IPv4\nAddress=192.168.1.20\nPort=500\n");
        Assert.Equal(BatcRedirect.FileState.Other, redirect.StateOf(own));

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
        var redirect = new BatcRedirect(_batc);
        File.WriteAllText(redirect.Files[0], "[SimConnect]\nProtocol=IPv4\nAddress=127.0.0.1\nPort=5112");

        Assert.Equal(BatcRedirect.FileState.ClearanceLink, redirect.StateOf(redirect.Files[0]));
    }

    [Fact]
    public void A_file_that_cannot_be_written_is_a_plain_message_not_a_crash()
    {
        // A directory where the cfg should go stands in for a read-only folder (tests may run as root).
        var redirect = new BatcRedirect(_batc);
        Directory.CreateDirectory(redirect.Files[1]);

        var ex = Assert.Throws<RedirectFailedException>(() => redirect.SwitchOn());
        Assert.Equal(redirect.Files[1], ex.FilePath);
        Assert.StartsWith($"could not write {redirect.Files[1]}: run ClearanceLink as administrator, or use connection automatic", ex.Message);

        var problem = Cli.ApplyConnection(new AppSettings { Connection = ConnectionMode.AlwaysOn }, _batc);
        Assert.NotNull(problem);
        Assert.StartsWith("could not write ", problem);
        Assert.Equal(Cli.BatcNotFoundProblem, Cli.ApplyConnection(new AppSettings { Connection = ConnectionMode.AlwaysOn }, null));
    }

    [Fact]
    public void Can_write_is_false_for_a_folder_that_refuses_files()
    {
        var redirect = new BatcRedirect(_batc);
        Assert.True(redirect.CanWrite());
        Assert.All(Directory.GetFiles(_batc, "*", SearchOption.AllDirectories), f => Assert.EndsWith("BeyondATC.exe", f));

        // A directory where the probe file goes stands in for a protected folder (tests may run as root).
        Directory.CreateDirectory(Path.Combine(_batc, "BeyondATC_Data", "StreamingAssets", "ClearanceLink.write-test"));
        Assert.False(redirect.CanWrite());
    }

    [Fact]
    public void Link_points_BeyondATC_at_ClearanceLink_only_while_MSFS_is_closed_and_undoes_it_on_exit()
    {
        var redirect = new BatcRedirect(_batc);
        var log = new List<string>();
        var guards = 0;
        var link = new BatcLink(redirect, log.Add, () => guards++);

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
        Assert.Contains(log, l => l.StartsWith("batc: BeyondATC pointed at ClearanceLink", StringComparison.Ordinal));
        Assert.Contains(log, l => l.StartsWith("batc: ClearanceLink closing", StringComparison.Ordinal));
    }

    [Fact]
    public void Link_puts_back_a_users_own_file_on_exit()
    {
        var redirect = new BatcRedirect(_batc);
        const string own = "[SimConnect]\r\nProtocol=IPv4\r\nAddress=192.168.1.20\r\nPort=500\r\n";
        File.WriteAllText(redirect.Files[0], own);
        var link = new BatcLink(redirect, _ => { });

        link.Follow(msfsRunning: false);
        Assert.True(redirect.IsOn);

        link.Release(ConnectionMode.Automatic);
        Assert.Equal(own, File.ReadAllText(redirect.Files[0]));
        Assert.False(File.Exists(redirect.Files[1]));
    }

    [Fact]
    public void Link_reports_a_folder_it_cannot_write_without_throwing()
    {
        var redirect = new BatcRedirect(_batc);
        Directory.CreateDirectory(redirect.Files[1]);
        var log = new List<string>();
        var link = new BatcLink(redirect, log.Add);

        link.Follow(msfsRunning: false);

        Assert.StartsWith("could not write ", link.Problem);
        Assert.Contains(log, l => l.StartsWith("batc: could not write ", StringComparison.Ordinal));
    }

    [Fact]
    public void Always_on_puts_back_files_a_BeyondATC_update_removed()
    {
        // Issue #35: connection always on, both files gone, BeyondATC fell back to MSFS's pipe and could not connect.
        var redirect = new BatcRedirect(_batc);
        redirect.SwitchOn();
        var log = new List<string>();
        var link = new BatcLink(redirect, log.Add);

        link.KeepOn();
        Assert.Empty(log); // Already in place: nothing to say.

        redirect.Files.ToList().ForEach(File.Delete);
        link.KeepOn();
        Assert.True(redirect.IsOn);
        Assert.Contains(log, l => l.StartsWith("batc: always on; BeyondATC's config files were missing, written again", StringComparison.Ordinal));

        // Serve stopping in always on leaves them, and a late timer tick does nothing.
        link.Release(ConnectionMode.AlwaysOn);
        redirect.Files.ToList().ForEach(File.Delete);
        link.KeepOn();
        Assert.All(redirect.Files, f => Assert.False(File.Exists(f)));
    }

    [Fact]
    public void Always_on_keeps_trying_and_logs_a_problem_once()
    {
        var redirect = new BatcRedirect(_batc);
        Directory.CreateDirectory(redirect.Files[1]);
        var log = new List<string>();
        var link = new BatcLink(redirect, log.Add);

        link.KeepOn();
        link.KeepOn();

        Assert.StartsWith("could not write ", link.Problem);
        Assert.Single(log, l => l.StartsWith("batc: could not write ", StringComparison.Ordinal));

        Directory.Delete(redirect.Files[1]);
        link.KeepOn();
        Assert.Null(link.Problem);
        Assert.True(redirect.IsOn);
    }

    [Fact]
    public void Always_on_stops_once_the_command_line_switches_it_off()
    {
        var redirect = new BatcRedirect(_batc);
        var alwaysOn = false;
        var link = new BatcLink(redirect, _ => { });

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
        var redirect = new BatcRedirect(_batc);
        var link = new BatcLink(redirect, _ => { });

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
        Assert.Equal(release, BatcLink.GuardShouldRelease(mode, portInUse));
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

    public void Dispose() => Directory.Delete(_batc, recursive: true);
}

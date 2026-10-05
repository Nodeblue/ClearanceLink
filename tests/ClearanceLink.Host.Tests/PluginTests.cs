using System.Net;
using System.Net.Sockets;
using System.Text;
using ClearanceLink.Host.Traffic;

namespace ClearanceLink.Host.Tests;

public sealed class PluginTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("clearancelink-plugin").FullName;

    [Fact]
    public void Pos_line_matches_the_plugin_protocol()
    {
        var state = new TrafficState(-27.384, 153.117, 23.5, 13, 2.5, -1.25, 15.5, true, 1, 0.25, 0, 1.4, TrafficLights.Nav | TrafficLights.Beacon | TrafficLights.Taxi);

        Assert.Equal("pos 7 -27.3840000 153.1170000 23.5 13.00 2.50 -1.25 15.5 1 1 0.25 0 1 19", PluginLink.FormatPos(7, state));
    }

    [Fact]
    public void Reads_ground_status_and_errors_from_the_plugin()
    {
        var log = new List<string>();
        var link = new PluginLink("0.4.0", log.Add, listenPort: 0, pluginPort: FreePort());
        var errors = new List<(uint, string)>();
        link.AircraftError += (id, message) => errors.Add((id, message));

        link.Handle("ground 5 12.3");
        link.Handle("status 0.1.0 2 1234 C:\\X-Plane 12\\Custom Data\\CSL;D:\\Bluebell");
        link.Handle("error 6 no model found for B738");
        link.Handle("error 6 no model found for B738");
        link.Handle("nonsense from the future");

        Assert.True(link.TryGetGroundFeet(5, out var feet));
        Assert.Equal(12.3, feet);
        Assert.Equal(1234, link.Status!.Models);
        Assert.Equal(["C:\\X-Plane 12\\Custom Data\\CSL", "D:\\Bluebell"], link.Status.CslFolders);
        Assert.Equal(2, errors.Count);
        Assert.Single(log, l => l.Contains("not drawn", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sends_hello_and_traffic_and_receives_over_udp()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var plugin = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var pluginPort = ((IPEndPoint)plugin.Client.LocalEndPoint!).Port;
        await using var link = new PluginLink("0.4.0", _ => { }, listenPort: 0, pluginPort: pluginPort);
        link.Start();

        link.Add(3, "B738", "QFA", null, "VH-XZE");
        var lines = new List<string>();
        while (!lines.Contains("add 3 B738 QFA - VH-XZE") || !lines.Any(l => l.StartsWith("hello 0.4.0", StringComparison.Ordinal)))
        {
            var datagram = await plugin.ReceiveAsync(cts.Token);
            lines.AddRange(Encoding.UTF8.GetString(datagram.Buffer).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }

        var reply = Encoding.UTF8.GetBytes("ground 3 14.0\nstatus 0.1.0 1 900 -\n");
        await plugin.SendAsync(reply, new IPEndPoint(IPAddress.Loopback, link.ListenPort), cts.Token);
        while (link.Status is null || !link.TryGetGroundFeet(3, out _))
        {
            await Task.Delay(20, cts.Token);
        }

        Assert.Equal(900, link.Status.Models);
        Assert.Empty(link.Status.CslFolders);
    }

    [Fact]
    public void Install_failures_tell_x_plane_running_from_missing_permissions()
    {
        Assert.Equal(PluginInstall.Failure.XPlaneRunning, PluginInstall.Classify(new PluginInUseException("win.xpl", new IOException())));
        Assert.Equal(PluginInstall.Failure.XPlaneRunning, PluginInstall.Classify(new IOException("sharing", unchecked((int)0x80070020))));
        Assert.Equal(PluginInstall.Failure.NoPermission, PluginInstall.Classify(new UnauthorizedAccessException("Access denied")));
        Assert.Equal(PluginInstall.Failure.Other, PluginInstall.Classify(new IOException("There is not enough space on the disk.", unchecked((int)0x80070070))));

        var install = new PluginInstall(Path.Combine(_dir, "X-Plane 12"), _dir);
        Assert.StartsWith("no permission to write in ", install.Describe(new UnauthorizedAccessException("Access denied")));
        Assert.Contains("Close X-Plane", install.Describe(new PluginInUseException("win.xpl", new IOException())));
    }

    [Fact]
    public void Installing_while_the_plugin_is_loaded_throws_plugin_in_use()
    {
        var bundled = Path.Combine(_dir, "package", "X-Plane plugin", "ClearanceLink");
        Directory.CreateDirectory(Path.Combine(bundled, "64"));
        File.WriteAllText(Path.Combine(bundled, "64", "win.xpl"), "v1");
        var install = new PluginInstall(Path.Combine(_dir, "X-Plane 12"), bundled);
        install.Install();

        // X-Plane holding the binary open stands in for a loaded plugin.
        using var loaded = new FileStream(Path.Combine(install.Target, "64", "win.xpl"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var ex = Assert.ThrowsAny<IOException>(() => install.Install());

        Assert.Equal(PluginInstall.Failure.XPlaneRunning, PluginInstall.Classify(ex));
    }

    [Fact]
    public void Installs_updates_and_removes_the_plugin_keeping_the_users_csl_list()
    {
        var bundled = Path.Combine(_dir, "package", "X-Plane plugin", "ClearanceLink");
        Directory.CreateDirectory(Path.Combine(bundled, "64"));
        File.WriteAllText(Path.Combine(bundled, "64", "win.xpl"), "v1");
        File.WriteAllText(Path.Combine(bundled, "README.txt"), "readme");
        var xplane = Path.Combine(_dir, "X-Plane 12");
        var install = new PluginInstall(xplane, bundled);

        Assert.Equal(PluginInstall.State.NotInstalled, install.Check());
        install.Install();
        Assert.Equal(PluginInstall.State.Installed, install.Check());

        File.WriteAllText(Path.Combine(install.Target, PluginInstall.UserFile), "D:\\Bluebell");
        File.WriteAllText(Path.Combine(bundled, "64", "win.xpl"), "v2");
        Assert.Equal(PluginInstall.State.Outdated, install.Check());
        install.Install();
        Assert.Equal("v2", File.ReadAllText(Path.Combine(install.Target, "64", "win.xpl")));
        Assert.Equal("D:\\Bluebell", File.ReadAllText(Path.Combine(install.Target, PluginInstall.UserFile)));

        var removed = install.Remove();
        Assert.False(Directory.Exists(install.Target));
        Assert.Contains(removed, l => l.Contains("D:\\Bluebell", StringComparison.Ordinal));
        Assert.Equal(PluginInstall.State.NotBundled, new PluginInstall(xplane, Path.Combine(_dir, "none")).Check());
    }

    [Fact]
    public void Installing_replaces_the_xpconnect_plugin_and_keeps_its_user_files()
    {
        var bundled = Path.Combine(_dir, "package", "X-Plane plugin", "ClearanceLink");
        Directory.CreateDirectory(Path.Combine(bundled, "64"));
        File.WriteAllText(Path.Combine(bundled, "64", "win.xpl"), "new");
        var xplane = Path.Combine(_dir, "X-Plane 12");
        var install = new PluginInstall(xplane, bundled);
        Directory.CreateDirectory(Path.Combine(install.OldTarget, "64"));
        Directory.CreateDirectory(Path.Combine(install.OldTarget, "Resources", "CSL", "MyModels"));
        File.WriteAllText(Path.Combine(install.OldTarget, "64", "win.xpl"), "old");
        File.WriteAllText(Path.Combine(install.OldTarget, PluginInstall.UserFile), "D:\\Bluebell");
        File.WriteAllText(Path.Combine(install.OldTarget, "settings.txt"), "labels=1");
        File.WriteAllText(Path.Combine(install.OldTarget, "Resources", "CSL", "MyModels", "xsb_aircraft.txt"), "EXPORT_NAME Mine");

        Assert.EndsWith(Path.Combine("plugins", "XPConnect"), install.OldTarget, StringComparison.Ordinal);
        Assert.Equal(PluginInstall.State.Outdated, install.Check());
        var lines = install.Install();

        Assert.False(Directory.Exists(install.OldTarget));
        Assert.Contains(lines, l => l.Contains("old XPConnect plugin", StringComparison.Ordinal));
        Assert.Equal("new", File.ReadAllText(Path.Combine(install.Target, "64", "win.xpl")));
        Assert.Equal("D:\\Bluebell", File.ReadAllText(Path.Combine(install.Target, PluginInstall.UserFile)));
        Assert.Equal("labels=1", File.ReadAllText(Path.Combine(install.Target, "settings.txt")));
        Assert.True(File.Exists(Path.Combine(install.Target, "Resources", "CSL", "MyModels", "xsb_aircraft.txt")));
        Assert.Equal(PluginInstall.State.Installed, install.Check());
    }

    [Fact]
    public void Installing_while_xplane_has_the_old_plugin_loaded_changes_nothing()
    {
        var bundled = Path.Combine(_dir, "package", "X-Plane plugin", "ClearanceLink");
        Directory.CreateDirectory(Path.Combine(bundled, "64"));
        File.WriteAllText(Path.Combine(bundled, "64", "win.xpl"), "new");
        var install = new PluginInstall(Path.Combine(_dir, "X-Plane 12"), bundled);
        Directory.CreateDirectory(Path.Combine(install.OldTarget, "64"));
        var oldXpl = Path.Combine(install.OldTarget, "64", "win.xpl");
        File.WriteAllText(oldXpl, "old");

        using (new FileStream(oldXpl, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => install.Install());
        }

        Assert.False(Directory.Exists(install.Target));
        Assert.True(File.Exists(oldXpl));
    }

    [Fact]
    public void Removing_shows_the_csl_list_from_the_old_xpconnect_plugin()
    {
        var install = new PluginInstall(Path.Combine(_dir, "X-Plane 12"), Path.Combine(_dir, "none"));
        Directory.CreateDirectory(install.OldTarget);
        File.WriteAllText(Path.Combine(install.OldTarget, PluginInstall.UserFile), "D:\\Bluebell");

        var lines = install.Remove();

        Assert.False(Directory.Exists(install.OldTarget));
        Assert.Contains(lines, l => l.Contains("D:\\Bluebell", StringComparison.Ordinal));
    }

    private static int FreePort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

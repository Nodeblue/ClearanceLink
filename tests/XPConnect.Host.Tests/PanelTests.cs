using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using XPConnect.Host.Panel;
using XPConnect.Host.Support;
using XPConnect.Host.Traffic;

[assembly: AvaloniaTestApplication(typeof(XPConnect.Host.Tests.PanelTestApp))]

namespace XPConnect.Host.Tests;

public static class PanelTestApp
{
    // Skia rendering (not headless drawing) so the panel can be captured as a picture.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<PanelApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class PanelTests
{
    private static readonly PanelState Ready = new(
        Version: "0.4.0",
        Serving: true,
        XPlaneConnected: true,
        XPlaneVersion: "12.4.3",
        BatcHelper: true,
        BatcApp: true,
        OtherAddOns: 1,
        LastAirport: "YBBN",
        LastAirportSource: "Orbx YBBN",
        MsfsRunning: false,
        Connection: ConnectionMode.Automatic,
        OldConfigFiles: false,
        SessionLog: true,
        SessionLogBytes: 2_500_000,
        KeepDays: 7,
        Uptime: TimeSpan.FromMinutes(72),
        BatcFolder: @"C:\BeyondATC",
        XPlaneFolder: @"C:\X-Plane 12");

    public static TheoryData<PanelPage> AllPages => new(Enum.GetValues<PanelPage>());

    [Theory]
    [MemberData(nameof(AllPages))]
    public void Every_line_fits_the_24_column_screen(PanelPage page)
    {
        var long_ = Ready with { BatcFolder = @"D:\Games\Flight Sim Add-ons\BeyondATC Install", LastAirportSource = "Global Airports (X-Plane 12 default scenery)" };
        foreach (var state in new[] { Ready, long_, Ready with { BatcFolder = null, Serving = false } })
        {
            var built = CduPages.Build(page, state);
            Assert.True(built.Title.Length <= CduPages.Width, built.Title);
            Assert.Equal(6, built.Rows.Count);
            Assert.All(built.Rows, row =>
            {
                var width = row.Left.Text.Length + row.Right.Text.Length + (row.Right.Text.Length > 0 ? 1 : 0);
                Assert.True(width <= CduPages.Width, $"{row.Left.Text}|{row.Right.Text}");
                Assert.True(row.LabelLeft.Length + row.LabelRight.Length < CduPages.Width + 8, row.LabelLeft);
            });
            Assert.True(built.Scratchpad.Text.Length <= CduPages.Width, built.Scratchpad.Text);
        }
    }

    [Theory]
    [InlineData(false, true, false, false, false, "STARTING...")]
    [InlineData(true, true, false, false, false, "CLOSE MSFS TO CONNECT")]
    [InlineData(true, false, false, false, false, "START X-PLANE 12")]
    [InlineData(true, false, true, false, false, "START BEYONDATC")]
    [InlineData(true, false, true, true, false, "BEYONDATC CONNECTING")]
    [InlineData(true, false, false, true, true, "X-PLANE NOT CONNECTED")]
    [InlineData(true, true, true, true, true, "READY FOR CLEARANCE")]
    public void Scratchpad_says_what_to_do_next(bool serving, bool msfs, bool xplane, bool helper, bool app, string expected)
    {
        var state = Ready with { Serving = serving, MsfsRunning = msfs, XPlaneConnected = xplane, BatcHelper = helper, BatcApp = app };

        Assert.Equal(expected, CduPages.Advice(state).Text);
    }

    [Fact]
    public void Setup_offers_to_remove_old_files_only_in_automatic_mode()
    {
        var old = Ready with { OldConfigFiles = true };

        Assert.Contains(CduPages.Build(PanelPage.Setup, old).Rows, r => r.KeyRight == CduKey.RemoveOldFiles);
        Assert.DoesNotContain(CduPages.Build(PanelPage.Setup, old with { Connection = ConnectionMode.AlwaysOn }).Rows, r => r.KeyRight == CduKey.RemoveOldFiles);
    }

    [Fact]
    public void Traffic_add_on_is_offered_on_setup_and_its_aircraft_shown_on_status()
    {
        var missing = Ready with { TrafficAddOn = PluginInstall.State.NotInstalled };
        Assert.Contains(CduPages.Build(PanelPage.Setup, missing).Rows, r => r.KeyRight == CduKey.InstallTrafficAddOn && r.Right.Text == "INSTALL>");
        Assert.Contains(CduPages.Build(PanelPage.Status, missing).Rows, r => r.Right.Text == "SEE SETUP");

        // Old BeyondATC files take that row until they are removed.
        Assert.DoesNotContain(CduPages.Build(PanelPage.Setup, missing with { OldConfigFiles = true }).Rows, r => r.KeyRight == CduKey.InstallTrafficAddOn);

        var drawing = Ready with { TrafficAddOn = PluginInstall.State.Installed, TrafficAircraft = 42 };
        Assert.Contains(CduPages.Build(PanelPage.Status, drawing).Rows, r => r.Right.Text == "42 AIRCRAFT");
        Assert.DoesNotContain(CduPages.Build(PanelPage.Setup, drawing).Rows, r => r.KeyRight == CduKey.InstallTrafficAddOn);
    }

    [Fact]
    public void Getting_started_pages_are_whole_and_say_the_project_is_unofficial()
    {
        foreach (var page in CduPages.StartPages)
        {
            var built = CduPages.Build(page, Ready with { TrafficAddOn = PluginInstall.State.NotInstalled });
            Assert.StartsWith("GETTING STARTED ", built.Title, StringComparison.Ordinal);
            Assert.All(built.Rows, row => Assert.DoesNotContain("~", row.Left.Text + row.Right.Text, StringComparison.Ordinal));
            Assert.All(built.Rows, row => Assert.True(row.LabelLeft.Length + row.LabelRight.Length + (row.LabelRight.Length > 0 ? 1 : 0) <= CduPages.Width, row.LabelLeft));
        }

        var welcome = CduPages.Build(PanelPage.Welcome, Ready);
        Assert.Contains(welcome.Rows, r => r.Left.Text == "ENDORSED BY BEYONDATC");
        Assert.Contains(welcome.Rows, r => r.Left.Text == "USE AT YOUR OWN RISK");

        var missing = CduPages.Build(PanelPage.StartFolders, Ready with { XPlaneFolder = null });
        Assert.Contains(missing.Rows, r => r.KeyRight == CduKey.ChooseXPlaneFolder && r.Right.Text == "CHOOSE>");
        Assert.Contains(CduPages.Build(PanelPage.StartTraffic, Ready).Rows, r => r.Left.Text == "NONE FOUND" && r.KeyRight == CduKey.OpenModelsHelp);
        Assert.Contains(CduPages.Build(PanelPage.StartTraffic, Ready with { CslPackages = 13 }).Rows, r => r.Left.Text == "13 PACKAGES");
        Assert.Contains(CduPages.Build(PanelPage.StartTraffic, Ready with { TrafficAddOn = PluginInstall.State.NotInstalled }).Rows, r => r.KeyRight == CduKey.InstallTrafficAddOn);
        Assert.Equal(CduKey.FinishGettingStarted, CduPages.Build(PanelPage.StartDone, Ready).Rows[5].KeyRight);
    }

    [AvaloniaFact]
    public void Getting_started_steps_forward_and_back_and_reopens_from_support()
    {
        var window = new PanelWindow(() => Ready) { StartWithGettingStarted = true };
        window.Show();

        Assert.Equal(PanelPage.Welcome, window.Page);
        Assert.Equal("GETTING STARTED 1/5", window.Shown!.Title);
        foreach (var expected in new[] { PanelPage.StartFolders, PanelPage.StartTraffic, PanelPage.StartXPlane, PanelPage.StartDone })
        {
            window.KeyPressQwerty(PhysicalKey.Digit6, RawInputModifiers.Shift);
            Assert.Equal(expected, window.Page);
        }

        window.KeyPressQwerty(PhysicalKey.Digit6, RawInputModifiers.None);
        Assert.Equal(PanelPage.StartXPlane, window.Page);
        window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
        window.KeyPressQwerty(PhysicalKey.Digit5, RawInputModifiers.None);
        Assert.Equal(PanelPage.Welcome, window.Page);

        // SKIP on the first page (no server behind this window, so it just goes to STATUS).
        window.KeyPressQwerty(PhysicalKey.Digit6, RawInputModifiers.None);
        Assert.Equal(PanelPage.Status, window.Page);
        window.Close();
    }

    [Fact]
    public void Csl_packages_are_counted_where_the_traffic_add_on_looks()
    {
        var root = Path.Combine(Path.GetTempPath(), "xpc-csl-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(0, PanelController.CountCslPackages(root));
            // Bluebell unzipped into one folder holding its packages, plus a package straight in the add-on's folder.
            foreach (var package in new[] { Path.Combine(root, "Custom Data", "CSL", "BB", "BB_Airbus"), Path.Combine(root, "Custom Data", "CSL", "BB", "BB_Boeing"),
                Path.Combine(root, "Resources", "plugins", "XPConnect", "Resources", "CSL", "Extra") })
            {
                Directory.CreateDirectory(package);
                File.WriteAllText(Path.Combine(package, "xsb_aircraft.txt"), "EXPORT_NAME test");
            }

            Directory.CreateDirectory(Path.Combine(root, "Custom Data", "CSL", "Empty"));
            Assert.Equal(3, PanelController.CountCslPackages(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Long_folders_keep_the_drive_and_the_last_folder()
    {
        Assert.Equal(@"C:\BeyondATC", CduPages.ShortPath(@"C:\BeyondATC\", 18));
        Assert.Equal(@"D:\...\BeyondATC", CduPages.ShortPath(@"D:\Games\Flight Sim Add-ons\BeyondATC", 18));
    }

    [AvaloniaFact]
    public void Line_select_keys_and_keyboard_change_page()
    {
        var window = new PanelWindow(() => Ready);
        window.Show();

        Assert.Equal("XPCONNECT STATUS", window.Shown!.Title);
        window.KeyPressQwerty(PhysicalKey.Digit6, RawInputModifiers.None);
        Assert.Equal(PanelPage.Setup, window.Page);
        window.KeyPressQwerty(PhysicalKey.Digit6, RawInputModifiers.Shift);
        Assert.Equal(PanelPage.About, window.Page);
        window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
        Assert.Equal(PanelPage.Support, window.Page);
        Assert.Equal("<CREATE SUPPORT FILE", window.Shown!.Rows[2].Left.Text);
        Assert.Equal(CduKey.ShowGettingStarted, window.Shown!.Rows[4].KeyLeft);
        window.Close();
    }

    [AvaloniaFact]
    public void Panel_renders_every_page()
    {
        var folder = Environment.GetEnvironmentVariable("XPCONNECT_SCREENSHOTS");
        var window = new PanelWindow(() => Ready);
        window.Show();
        foreach (var page in Enum.GetValues<PanelPage>())
        {
            window.ShowPage(page);
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.True(frame!.Size.Width > 300 && frame.Size.Height > 400, $"{frame.Size}");
            if (folder is not null)
            {
                Directory.CreateDirectory(folder);
                frame.Save(Path.Combine(folder, $"panel-{page.ToString().ToLowerInvariant()}.png"));
            }
        }

        window.Close();
    }
}

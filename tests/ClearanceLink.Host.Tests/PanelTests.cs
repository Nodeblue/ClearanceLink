using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ClearanceLink.Host;
using ClearanceLink.Host.Panel;
using ClearanceLink.Host.Pushback;
using ClearanceLink.Host.Support;
using ClearanceLink.Host.Traffic;

[assembly: AvaloniaTestApplication(typeof(ClearanceLink.Host.Tests.PanelTestApp))]

namespace ClearanceLink.Host.Tests;

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
        Version: "0.5.0",
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
        BatcRedirected: false,
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
        var update = Ready with { Update = new UpdateInfo("10.12.13-beta.4", "https://github.com/Nodeblue/ClearanceLink/releases/tag/v10.12.13") };
        var fsltl = Ready with { FsltlFolder = @"C:\MSFS\Community\fsltl-traffic-base", FsltlLiveries = 3112, XPlaneRunning = true };
        var converting = fsltl with { FsltlConverting = true, FsltlProgress = new Fsltl.ImportProgress(Fsltl.ImportStage.Textures, 12345, 99999) };
        foreach (var state in new[] { Ready, long_, Ready with { BatcFolder = null, Serving = false }, update, fsltl with { FsltlOutOfDate = true }, converting, Ready with { MsfsInstalled = false } })
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

    [Fact]
    public void A_new_release_takes_turns_with_the_next_step_before_a_flight_only()
    {
        var update = new UpdateInfo("0.6.0", "https://github.com/Nodeblue/ClearanceLink/releases/tag/v0.6.0");
        var waiting = Ready with { BatcHelper = false, BatcApp = false, Update = update };
        var turn = TimeSpan.FromSeconds(CduPages.UpdateTurnSeconds);

        Assert.Equal("START BEYONDATC", CduPages.Advice(waiting with { Uptime = turn * 2 }).Text);
        var notice = CduPages.Advice(waiting with { Uptime = turn * 3 });
        Assert.Equal("NEW VERSION 0.6.0", notice.Text);
        Assert.Equal(CduColor.Magenta, notice.Color);

        // Never over something to fix, and not once BeyondATC is connected (green).
        Assert.Equal("CLOSE MSFS TO CONNECT", CduPages.Advice(waiting with { MsfsRunning = true, Uptime = turn * 3 }).Text);
        Assert.Equal("READY FOR CLEARANCE", CduPages.Advice(Ready with { Update = update, Uptime = turn * 3 }).Text);
        // Fits the screen with the longest version the tests use elsewhere.
        Assert.True(CduPages.Advice(waiting with { Uptime = turn * 3, Update = update with { Version = "10.12.13-beta.4" } }).Text.Length <= CduPages.Width);
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

    [AvaloniaFact]
    public void Window_and_exe_icon_has_every_size_windows_asks_for()
    {
        using var icon = Avalonia.Platform.AssetLoader.Open(new Uri("avares://ClearanceLink/Assets/ClearanceLink.ico"));
        using var reader = new BinaryReader(icon);
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16()); // icon, not cursor
        var sizes = Enumerable.Range(0, reader.ReadUInt16()).Select(_ =>
        {
            var entry = reader.ReadBytes(16);
            return entry[0] == 0 ? 256 : entry[0];
        }).ToList();
        Assert.Subset(sizes.ToHashSet(), new HashSet<int> { 16, 24, 32, 48, 256 });
        Assert.NotNull(PanelWindow.AppIcon());
    }

    [Fact]
    public void The_help_mark_sits_beside_the_label_it_explains()
    {
        // SUPPORT's HELP row has its label on the right only: the "i" goes before it, not at the far left.
        var help = Assert.Single(CduPages.Build(PanelPage.Support, Ready).Rows, r => r.KeyRight == CduKey.OpenHelp);
        Assert.Equal(HelpMark.Right, help.Mark);
        Assert.Equal(HelpMark.Left, new CduRow("A", "B", new(""), new("")) { Help = "x" }.Mark);
        Assert.Equal(HelpMark.None, new CduRow("", "", new("<STATUS"), new("")) { Help = "x" }.Mark);
        Assert.Equal(HelpMark.None, new CduRow("A", "", new(""), new("")).Mark);

        // Every page: a row with help and a label has a mark on the side that has the label.
        foreach (var page in Enum.GetValues<PanelPage>())
        {
            foreach (var row in CduPages.Build(page, Ready).Rows.Where(r => r.Help is not null && r.LabelLeft.Length + r.LabelRight.Length > 0))
            {
                Assert.Equal(row.LabelLeft.Length > 0 ? HelpMark.Left : HelpMark.Right, row.Mark);
            }
        }
    }

    [AvaloniaFact]
    public void Help_shows_on_the_i_only_not_on_the_whole_row()
    {
        var window = new PanelWindow(() => Ready);
        window.ShowPage(PanelPage.Support);
        var tips = Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(window).OfType<Avalonia.Controls.Control>()
            .Where(c => Avalonia.Controls.ToolTip.GetTip(c) is string).ToList();

        Assert.NotEmpty(tips);
        Assert.All(tips, c => Assert.Equal(12, c.Width)); // only the 12 px marks carry a tip
    }
    [Fact]
    public void Setup_says_where_BeyondATC_goes()
    {
        string Help(PanelState state) => CduPages.Build(PanelPage.Setup, state).Scratchpad.Text;

        Assert.Equal(CduPages.BatcUntilClose, Help(Ready with { BatcRedirected = true }));
        Assert.True(CduPages.BatcUntilClose.Length <= CduPages.Width);
        Assert.Equal("CLOSE MSFS BEFORE FLYING", Help(Ready with { BatcRedirected = true, MsfsRunning = true }));
        Assert.Equal("", Help(Ready));
        Assert.Equal("BATC SET TO X-PLANE", Help(Ready with { Connection = ConnectionMode.AlwaysOn, BatcRedirected = true }));
    }

    [Fact]
    public void Traffic_add_on_is_offered_on_setup_and_its_aircraft_shown_on_status()
    {
        var missing = Ready with { TrafficAddOn = PluginInstall.State.NotInstalled };
        Assert.Contains(CduPages.Build(PanelPage.Traffic, missing).Rows, r => r.KeyRight == CduKey.InstallTrafficAddOn && r.Right.Text == "INSTALL>");
        Assert.Contains(CduPages.Build(PanelPage.Setup, missing).Rows, r => r.Left.Text == "INSTALL PLUGIN" && r.KeyRight == CduKey.GoTraffic);
        Assert.Contains(CduPages.Build(PanelPage.Status, missing).Rows, r => r.Right.Text == "SET UP>" && r.KeyRight == CduKey.GoTraffic);

        var drawing = Ready with { TrafficAddOn = PluginInstall.State.Installed, TrafficAircraft = 42 };
        Assert.Contains(CduPages.Build(PanelPage.Status, drawing).Rows, r => r.Right.Text == "42 AIRCRAFT");
        Assert.DoesNotContain(CduPages.Build(PanelPage.Traffic, drawing).Rows, r => r.KeyRight == CduKey.InstallTrafficAddOn);
        Assert.Contains(CduPages.Build(PanelPage.Traffic, drawing).Rows, r => r.LabelRight == "AIRCRAFT" && r.Right.Text == "42 AIRCRAFT");
        Assert.Contains(CduPages.Build(PanelPage.Setup, drawing).Rows, r => r.Left.Text == "42 AIRCRAFT");
    }

    [Fact]
    public void Getting_started_pages_are_whole_and_say_the_project_is_unofficial()
    {
        foreach (var page in CduPages.StartPages)
        {
            var built = CduPages.Build(page, Ready with { TrafficAddOn = PluginInstall.State.NotInstalled });
            Assert.StartsWith("WELCOME ", built.Title, StringComparison.Ordinal);
            Assert.NotEmpty(built.Message!);
            Assert.All(built.Rows, row => Assert.DoesNotContain("~", row.Left.Text + row.Right.Text, StringComparison.Ordinal));
            Assert.All(built.Rows, row => Assert.True(row.LabelLeft.Length + row.LabelRight.Length + (row.LabelRight.Length > 0 ? 1 : 0) <= CduPages.Width, row.LabelLeft));
        }

        var welcome = CduPages.Build(PanelPage.Welcome, Ready);
        Assert.Contains(welcome.Message!, m => m.Text.Contains("Not affiliated with or endorsed by BeyondATC.", StringComparison.Ordinal));
        Assert.Contains(welcome.Message!, m => m.Text.Contains("no warranty", StringComparison.Ordinal));

        // Advice is text, not rows that look selectable: the X-Plane settings page has keys only for paging.
        var xplane = CduPages.Build(PanelPage.StartXPlane, Ready);
        Assert.All(xplane.Rows.Take(5), r => Assert.Equal(CduRow.Empty, r));
        Assert.Contains(xplane.Message!, m => m.Text.Contains("Sound: Radios to 0", StringComparison.Ordinal) && m.Text.Contains("Draw parked aircraft off", StringComparison.Ordinal));

        // BeyondATC flies the user's latest SimBrief plan from where they are parked, so that comes first.
        var done = CduPages.Build(PanelPage.StartDone, Ready);
        Assert.Contains(done.Message!, m => m.Text.Contains("1. Plan it in SimBrief", StringComparison.Ordinal) && m.Text.Contains("SimBrief departure", StringComparison.Ordinal));

        // ABOUT is a message too; only the keys are rows.
        var about = CduPages.Build(PanelPage.About, Ready);
        Assert.All(about.Rows.Take(5), r => Assert.Equal(CduRow.Empty, r));
        Assert.Contains(about.Message!, m => m.Text.Contains("Not affiliated with BeyondATC.", StringComparison.Ordinal));
        Assert.Contains(CduPages.Build(PanelPage.Status, Ready).Rows, r => r.LabelLeft == "ATC AIRPORT" && r.LabelRight == "SCENERY");

        var missing = CduPages.Build(PanelPage.StartFolders, Ready with { XPlaneFolder = null });
        Assert.Contains(missing.Rows, r => r.KeyRight == CduKey.ChooseXPlaneFolder && r.Right.Text == "CHOOSE>");
        Assert.Contains(CduPages.Build(PanelPage.StartTraffic, Ready with { TrafficAddOn = PluginInstall.State.NotInstalled }).Rows, r => r.LabelLeft == "X-PLANE PLUGIN" && r.KeyRight == CduKey.InstallTrafficAddOn);
        Assert.Contains(CduPages.Build(PanelPage.StartTraffic, Ready).Message!, m => m.Text.Contains("no X-Plane plugin", StringComparison.Ordinal));

        // Both tested model libraries: Bluebell (count, help) and FSLTL (choose, convert, converted).
        Assert.Contains(CduPages.Build(PanelPage.StartModels, Ready).Rows, r => r.LabelLeft == "CSL MODELS" && r.Left.Text == "NOT FOUND" && r.KeyRight == CduKey.OpenModelsHelp);
        Assert.Contains(CduPages.Build(PanelPage.StartModels, Ready with { CslPackages = 13 }).Rows, r => r.Left.Text == "13 PACKAGES");
        Assert.Contains(CduPages.Build(PanelPage.StartModels, Ready).Rows, r => r.LabelLeft == "FSLTL" && r.KeyRight == CduKey.ChooseFsltlFolder);
        var fsltl = Ready with { FsltlFolder = @"C:\MSFS\Community\fsltl-traffic-base" };
        Assert.Contains(CduPages.Build(PanelPage.StartModels, fsltl).Rows, r => r.Left.Text == "FOUND IN MSFS" && r.KeyRight == CduKey.ConvertFsltl);
        Assert.Contains(CduPages.Build(PanelPage.StartModels, fsltl with { FsltlLiveries = 812 }).Rows, r => r.Left.Text == "812 LIVERIES" && r.Right.Text == "UPDATE>");
        // Converted earlier but the folder isn't known now (no MSFS): CHOOSE, so BeyondATC can be shown it (ADR-0014).
        Assert.Contains(CduPages.Build(PanelPage.StartModels, Ready with { FsltlLiveries = 812 }).Rows, r => r.Left.Text == "812 LIVERIES" && r.Right.Text == "CHOOSE>" && r.KeyRight == CduKey.ChooseFsltlFolder);
        Assert.Contains(CduPages.Build(PanelPage.StartModels, fsltl with { FsltlConverting = true }).Rows, r => r.Left.Text == "CONVERTING..." && r.KeyRight == CduKey.None);
        Assert.Equal(CduKey.FinishGettingStarted, CduPages.Build(PanelPage.StartDone, Ready).Rows[5].KeyRight);
    }

    [Theory]
    [MemberData(nameof(AllPages))]
    public void Messages_fit_above_the_first_row_with_content(PanelPage page)
    {
        foreach (var state in new[] { Ready, Ready with { BatcFolder = null }, Ready with { TrafficAddOn = PluginInstall.State.NotInstalled }, Ready with { Update = new UpdateInfo("10.12.13-beta.4", "https://example.invalid/") }, Ready with { MsfsInstalled = false } })
        {
            var built = CduPages.Build(page, state);
            if (built.Message is null)
            {
                continue;
            }

            // 15 px text wraps at about 28 characters of the 24-column screen; a row (label + data) is 47 px, a text line 20.25 px, under a 4 px margin.
            var lines = built.Message.Sum(m => m.Text.Split('\n').Sum(WrappedLines)) + built.Message.Count - 1;
            var room = (int)(((built.Rows.TakeWhile(r => r == CduRow.Empty).Count() * 47) - 4) / 20.25);
            Assert.True(lines <= room, $"{page}: {lines} lines, room for {room}");
        }
    }

    private static int WrappedLines(string paragraph)
    {
        var lines = 1;
        var used = 0;
        foreach (var word in paragraph.Split(' '))
        {
            if (used > 0 && used + 1 + word.Length > 28)
            {
                lines++;
                used = 0;
            }

            used += (used > 0 ? 1 : 0) + word.Length;
        }

        return lines;
    }

    [AvaloniaFact]
    public void Getting_started_steps_forward_and_back_and_reopens_from_setup()
    {
        var window = new PanelWindow(() => Ready) { StartWithGettingStarted = true };
        window.Show();

        Assert.Equal(PanelPage.Welcome, window.Page);
        Assert.Equal("WELCOME 1/6", window.Shown!.Title);
        foreach (var expected in new[] { PanelPage.StartFolders, PanelPage.StartTraffic, PanelPage.StartModels, PanelPage.StartXPlane, PanelPage.StartDone })
        {
            window.KeyPressQwerty(PhysicalKey.Digit6, RawInputModifiers.Shift);
            Assert.Equal(expected, window.Page);
        }

        window.KeyPressQwerty(PhysicalKey.Digit6, RawInputModifiers.None);
        Assert.Equal(PanelPage.StartXPlane, window.Page);
        window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
        window.KeyPressQwerty(PhysicalKey.Digit6, RawInputModifiers.None);
        Assert.Equal(PanelPage.Welcome, window.Page);

        // SKIP on the first page (no server behind this window, so it just goes to STATUS).
        window.KeyPressQwerty(PhysicalKey.Digit6, RawInputModifiers.None);
        Assert.Equal(PanelPage.Status, window.Page);
        window.Close();
    }

    [Fact]
    public void Csl_packages_are_counted_where_the_traffic_add_on_looks()
    {
        var root = Path.Combine(Path.GetTempPath(), "clink-csl-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(0, PanelController.CountCslPackages(root));
            // Bluebell unzipped into one folder holding its packages, plus a package straight in the add-on's folder.
            foreach (var package in new[] { Path.Combine(root, "Custom Data", "CSL", "BB", "BB_Airbus"), Path.Combine(root, "Custom Data", "CSL", "BB", "BB_Boeing"),
                Path.Combine(root, "Resources", "plugins", "ClearanceLink", "Resources", "CSL", "Extra") })
            {
                Directory.CreateDirectory(package);
                File.WriteAllText(Path.Combine(package, "xsb_aircraft.txt"), "EXPORT_NAME test");
            }

            Directory.CreateDirectory(Path.Combine(root, "Custom Data", "CSL", "Empty"));
            Assert.Equal(3, PanelController.CountCslPackages(root));

            // Converted FSLTL models are counted on their own, not as Bluebell-style packages.
            var fsltl = Path.Combine(root, "Custom Data", "CSL", "ClearanceLink-FSLTL");
            Directory.CreateDirectory(fsltl);
            File.WriteAllText(Path.Combine(fsltl, "xsb_aircraft.txt"), "EXPORT_NAME ClearanceLink_FSLTL");
            Assert.Equal(3, PanelController.CountCslPackages(root, except: fsltl));
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

        Assert.Equal("CLEARANCELINK STATUS", window.Shown!.Title);
        window.KeyPressQwerty(PhysicalKey.Digit6, RawInputModifiers.None);
        Assert.Equal(PanelPage.Setup, window.Page);
        window.KeyPressQwerty(PhysicalKey.Digit6, RawInputModifiers.Shift);
        Assert.Equal(PanelPage.About, window.Page);
        window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
        Assert.Equal(PanelPage.Support, window.Page);
        Assert.Equal("<CREATE SUPPORT FILE", window.Shown!.Rows[2].Left.Text);
        Assert.Equal(CduKey.OpenHelp, window.Shown!.Rows[4].KeyRight);
        window.Close();
    }

    [Fact]
    public void Beyondatc_finding_no_fsltl_is_an_amber_warning_on_traffic_setup_and_status()
    {
        // BeyondATC creates no traffic at all without FSLTL (ADR-0014): say so where traffic is set up and shown.
        var none = Ready with { TrafficAddOn = PluginInstall.State.Installed, BatcModels = BatcTrafficModels.Outcome.NoFsltl };
        var warning = Assert.Single(CduPages.Build(PanelPage.Traffic, none).Rows, r => r.LabelLeft == "BEYONDATC TRAFFIC");
        Assert.Equal(("NO FSLTL", CduColor.Amber, "CHECK>", CduKey.GoFsltl), (warning.Left.Text, warning.Left.Color, warning.Right.Text, warning.KeyRight));
        Assert.False(string.IsNullOrEmpty(warning.Help));

        // Issue #29: FSLTL converted but not in MSFS 2024's Community folder says where it's missing, not "NO FSLTL".
        var notIn2024 = Assert.Single(CduPages.Build(PanelPage.Traffic, none with { BatcModels = BatcTrafficModels.Outcome.NotInMsfs2024 }).Rows, r => r.LabelLeft == "BEYONDATC TRAFFIC");
        Assert.Equal(("NOT IN MSFS 2024", CduColor.Amber), (notIn2024.Left.Text, notIn2024.Left.Color));
        var sees = Assert.Single(CduPages.Build(PanelPage.Traffic, none with { BatcModels = BatcTrafficModels.Outcome.Pointed }).Rows, r => r.LabelLeft == "BEYONDATC TRAFFIC");
        Assert.Equal(("SEES FSLTL", CduColor.Green), (sees.Left.Text, sees.Left.Color));

        var status = CduPages.Build(PanelPage.Status, none).Rows[4];
        Assert.Equal(("NO FSLTL", CduColor.Amber, CduKey.GoTraffic), (status.Right.Text, status.Right.Color, status.KeyRight));
        Assert.Contains(CduPages.Build(PanelPage.Setup, none).Rows, r => r.Right.Text == "NO FSLTL" || r.Left.Text == "NO FSLTL");
    }

    [Fact]
    public void Fsltl_check_says_what_the_folder_holds_and_what_to_do()
    {
        var folder = @"C:\MSFS\Community\fsltl-traffic-base";
        var good = Ready with { FsltlFolder = folder, FsltlLiveries = 3350, BatcModels = BatcTrafficModels.Outcome.Found, FsltlHealth = new(3350, 98, 0, "1.5.0") };
        var page = CduPages.Build(PanelPage.Fsltl, good);
        Assert.Equal("FSLTL CHECK", page.Title);
        Assert.Equal(("3350", "98"), (page.Rows[1].Left.Text, page.Rows[1].Right.Text));
        Assert.Equal((@"C:\...\COMMUNITY", "VERSION 1.5.0", "CHOOSE>"), (page.Rows[0].Left.Text, page.Rows[0].LabelRight, page.Rows[0].Right.Text));
        Assert.Equal(("SEES IT", "IN MSFS 2024"), (page.Rows[2].Left.Text, page.Rows[2].Right.Text));
        Assert.Equal(("3350 LIVERIES", CduKey.ConvertFsltl), (page.Rows[4].Left.Text, page.Rows[4].KeyRight));
        Assert.Equal(("FSLTL READY", CduColor.Green), (page.Scratchpad.Text, page.Scratchpad.Color));
        Assert.Contains(page.Rows, r => r.KeyRight == CduKey.ChooseFsltlFolder);
        Assert.Contains(page.Rows, r => r.KeyLeft == CduKey.GoTraffic);
        Assert.All(page.Rows, r => Assert.True(r.LabelLeft.Length + r.LabelRight.Length <= CduPages.Width && r.Left.Text.Length + r.Right.Text.Length <= CduPages.Width, r.ToString()));

        // Liveries but no base models: nothing can be converted, and the FSLTL row on TRAFFIC sends the user here.
        var noModels = good with { FsltlLiveries = 0, FsltlHealth = new(3350, 0, 3350, null) };
        page = CduPages.Build(PanelPage.Fsltl, noModels);
        Assert.Equal(("NONE", CduColor.Amber), (page.Rows[1].Right.Text, page.Rows[1].Right.Color));
        Assert.Equal(("LIVERIES BUT NO MODELS", CduColor.Amber), (page.Scratchpad.Text, page.Scratchpad.Color));
        Assert.Equal("-", page.Rows[4].Left.Text);
        Assert.Contains(CduPages.Build(PanelPage.Traffic, noModels).Rows, r => r.LabelLeft == "FSLTL" && r.Left.Text == "NO BASE MODELS" && r.KeyRight == CduKey.GoFsltl);
        Assert.Contains(CduPages.Build(PanelPage.Setup, noModels).Rows, r => r.Left.Text == "CHECK FSLTL");

        // Some models missing: those liveries fall back to CSL models, nothing to fix, so it's still ready.
        var someMissing = good with { FsltlHealth = new(3350, 92, 120, "1.5.0") };
        page = CduPages.Build(PanelPage.Fsltl, someMissing);
        Assert.Equal(CduColor.Green, page.Rows[1].Right.Color);
        Assert.Equal(("READY: 120 HAVE NO MODEL", CduColor.Green), (page.Scratchpad.Text, page.Scratchpad.Color));
        // While converting, that comes second.
        Assert.Equal("KEEP THIS WINDOW OPEN", CduPages.Build(PanelPage.Fsltl, someMissing with { FsltlConverting = true }).Scratchpad.Text);

        // Not an FSLTL package at all.
        Assert.Equal("NOT AN FSLTL FOLDER", CduPages.Build(PanelPage.Fsltl, good with { FsltlHealth = new(0, 0, 0, null) }).Scratchpad.Text);

        // Converted fine, but MSFS 2024 is here without FSLTL in the Community folder BeyondATC reads (issue #29).
        page = CduPages.Build(PanelPage.Fsltl, good with { BatcModels = BatcTrafficModels.Outcome.NotInMsfs2024, BatcCommunity = @"C:\Users\Pilot\AppData\Roaming\Microsoft Flight Simulator 2024\Packages\Community" });
        Assert.Equal(("BEYONDATC LOOKS IN", @"C:\...\COMMUNITY", CduColor.Amber), (page.Rows[3].LabelLeft, page.Rows[3].Left.Text, page.Rows[3].Left.Color));
        Assert.Equal(("WON'T SEE IT", "NOT IN 2024"), (page.Rows[2].Left.Text, page.Rows[2].Right.Text));
        Assert.Equal("COPY FSLTL TO LOOKS IN", page.Scratchpad.Text);
        // MSFS 2024's settings name only a NextBoot folder: BeyondATC reads none.
        page = CduPages.Build(PanelPage.Fsltl, good with { BatcModels = BatcTrafficModels.Outcome.NotInMsfs2024 });
        Assert.Equal(("NOT SET IN MSFS 2024", CduColor.Amber), (page.Rows[3].Left.Text, page.Rows[3].Left.Color));

        // No MSFS 2024: ClearanceLink points BeyondATC at FSLTL while it's open.
        page = CduPages.Build(PanelPage.Fsltl, good with { BatcModels = BatcTrafficModels.Outcome.Pointed });
        Assert.Equal(("SEES IT", "WHILE OPEN"), (page.Rows[2].Left.Text, page.Rows[2].Right.Text));

        // Found but not converted yet; no folder at all.
        page = CduPages.Build(PanelPage.Fsltl, good with { FsltlLiveries = 0 });
        Assert.Equal(("NOT CONVERTED", "CONVERT>"), (page.Rows[4].Left.Text, page.Rows[4].Right.Text));
        Assert.Equal("READY TO CONVERT", page.Scratchpad.Text);
        page = CduPages.Build(PanelPage.Fsltl, Ready with { MsfsInstalled = false });
        Assert.Equal(("NOT FOUND", "CHOOSE YOUR FSLTL FOLDER"), (page.Rows[0].Left.Text, page.Scratchpad.Text));
        // With MSFS, FSLTL is installed through it; with MSFS 2024, it must go there for BeyondATC.
        Assert.Equal("GET FSLTL OR CHOOSE IT", CduPages.Build(PanelPage.Fsltl, Ready).Scratchpad.Text);
        Assert.Equal("GET FSLTL FOR MSFS 2024", CduPages.Build(PanelPage.Fsltl, Ready with { BatcModels = BatcTrafficModels.Outcome.NotInMsfs2024 }).Scratchpad.Text);
        Assert.Equal(CduKey.OpenModelsHelp, page.Rows[5].KeyRight);

        // Just after CHOOSE, the old folder's results aren't shown.
        page = CduPages.Build(PanelPage.Fsltl, noModels with { FsltlChecking = true });
        Assert.Equal(("CHECKING...", "-", "CHECKING FOLDER"), (page.Rows[0].Left.Text, page.Rows[1].Left.Text, page.Scratchpad.Text));

        // A folder that can't be read (a drive not connected), and a UserCfg.opt that can't be read.
        Assert.Equal("CAN'T READ THIS FOLDER", CduPages.Build(PanelPage.Fsltl, good with { FsltlHealth = null }).Scratchpad.Text);
        page = CduPages.Build(PanelPage.Fsltl, good with { BatcModels = BatcTrafficModels.Outcome.Failed });
        Assert.Equal(("CAN'T TELL", "BEYONDATC CHECK FAILED"), (page.Rows[2].Left.Text, page.Scratchpad.Text));

        // A conversion started here that failed later, or needs a restart, says so here as well as on TRAFFIC.
        page = CduPages.Build(PanelPage.Fsltl, good with { FsltlMessage = "CONVERT FAILED: SEE LOG" });
        Assert.Equal(("CONVERT FAILED: SEE LOG", CduColor.Amber), (page.Scratchpad.Text, page.Scratchpad.Color));
        Assert.Equal("RESTART TO USE MODELS", CduPages.Build(PanelPage.Fsltl, good with { FsltlMessage = "RESTART TO USE MODELS" }).Scratchpad.Text);
        Assert.Equal("FSLTL READY", CduPages.Build(PanelPage.Fsltl, good with { FsltlMessage = "3350 LIVERIES CONVERTED" }).Scratchpad.Text);

        // Liveries but no models, and also out of date: UPDATE would convert nothing, so SETUP and STATUS send to the check.
        var brokenAndOld = noModels with { FsltlLiveries = 3154, FsltlOutOfDate = true, TrafficAddOn = PluginInstall.State.Installed };
        Assert.Contains(CduPages.Build(PanelPage.Setup, brokenAndOld).Rows, r => r.Left.Text == "CHECK FSLTL");
        var status = CduPages.Build(PanelPage.Status, brokenAndOld).Rows[4];
        Assert.Equal(("CHECK FSLTL>", CduKey.GoFsltl), (status.Right.Text, status.KeyRight));
        // On TRAFFIC the same cell has no key of its own, so no ">".
        Assert.Contains(CduPages.Build(PanelPage.Traffic, good with { FsltlOutOfDate = true, TrafficAddOn = PluginInstall.State.Installed }).Rows,
            r => r.LabelRight == "AIRCRAFT" && r.Right.Text == "UPDATE");

        // Every bottom line fits.
        foreach (var state in new[] { good, noModels, someMissing, Ready, Ready with { MsfsInstalled = false }, good with { FsltlHealth = new(0, 0, 0, null) },
            good with { FsltlHealth = new(9999, 92, 999, null) }, good with { BatcModels = BatcTrafficModels.Outcome.NotInMsfs2024 }, good with { FsltlLiveries = 0, XPlaneRunning = true } })
        {
            Assert.True(CduPages.Build(PanelPage.Fsltl, state).Scratchpad.Text.Length <= CduPages.Width, state.ToString());
        }
    }

    [Fact]
    public void Csl_models_row_names_the_packages_installed()
    {
        Assert.Null(PanelController.CslNames([]));
        Assert.Equal("BLUEBELL", PanelController.CslNames([@"Custom Data\CSL\BB\BB_Airbus", @"Custom Data\CSL\BB\BB_Boeing1"]));
        Assert.Equal("BLUEBELL + X-CSL", PanelController.CslNames([@"Custom Data\CSL\X-CSL\A320", @"Custom Data\CSL\BB_Airbus"]));
        Assert.Equal("X-CSL + OTHER", PanelController.CslNames(["Custom Data/CSL/X-CSL/B738", "Resources/plugins/ClearanceLink/Resources/CSL/Extra"]));

        var row = Assert.Single(CduPages.Build(PanelPage.Traffic, Ready with { CslPackages = 14, CslNames = "BLUEBELL + X-CSL" }).Rows, r => r.LabelLeft == "CSL MODELS");
        Assert.Equal(("BLUEBELL + X-CSL", CduKey.OpenModelsHelp), (row.Left.Text, row.KeyRight));
        // Too long to fit beside HOW TO>: the count instead.
        Assert.Contains(CduPages.Build(PanelPage.Traffic, Ready with { CslPackages = 15, CslNames = "BLUEBELL + X-CSL + OTHER" }).Rows, r => r.Left.Text == "15 PACKAGES");
    }

    [Fact]
    public void Status_says_no_models_when_the_plugin_runs_without_a_csl_package()
    {
        // BATC flies its traffic and the plugin runs, but there is nothing to draw it with: say so, and the key opens
        // the aircraft models page. "0 AIRCRAFT" in green would read as all well.
        var noModels = Ready with { TrafficAddOn = PluginInstall.State.Installed, TrafficAircraft = 0, PluginModels = 0 };
        var row = CduPages.Build(PanelPage.Status, noModels).Rows[4];
        Assert.Equal("NO MODELS", row.Right.Text);
        Assert.Equal(CduColor.Amber, row.Right.Color);
        Assert.Equal(CduKey.GoModels, row.KeyRight);

        var drawing = noModels with { TrafficAircraft = 12, PluginModels = 2027 };
        Assert.Equal("12 AIRCRAFT", CduPages.Build(PanelPage.Status, drawing).Rows[4].Right.Text);
        Assert.Equal(CduKey.None, CduPages.Build(PanelPage.Status, drawing).Rows[4].KeyRight);
    }

    [Fact]
    public void Models_page_keeps_the_last_conversion_result_on_its_bottom_line()
    {
        var fsltl = Ready with { FsltlFolder = @"C:\MSFS\Community\fsltl-traffic-base", FsltlLiveries = 3112 };
        Assert.Equal("3112 LIVERIES CONVERTED", CduPages.Build(PanelPage.StartModels, fsltl with { FsltlMessage = "3112 LIVERIES CONVERTED" }).Scratchpad.Text);
        var failed = CduPages.Build(PanelPage.StartModels, fsltl with { FsltlMessage = "CONVERT FAILED: SEE LOG" }).Scratchpad;
        Assert.Equal(CduColor.Amber, failed.Color);
        Assert.Equal("FLASH", CduPages.Build(PanelPage.StartModels, fsltl with { FsltlMessage = "3112 LIVERIES CONVERTED" }, "FLASH").Scratchpad.Text);
    }

    [AvaloniaFact]
    public async Task No_models_key_opens_the_traffic_page()
    {
        var window = new PanelWindow(() => Ready with { TrafficAddOn = PluginInstall.State.Installed, TrafficAircraft = 0, PluginModels = 0 });
        window.Show();
        await window.PressAsync(4, right: true);
        Assert.Equal(PanelPage.Traffic, window.Page);
        window.Close();
    }

    [AvaloniaFact]
    public void Panel_renders_every_page()
    {
        var folder = Environment.GetEnvironmentVariable("CLEARANCELINK_SCREENSHOTS");

        // A typical first run: plugin not yet installed, Bluebell installed, FSLTL found but not converted.
        var firstRun = Ready with
        {
            TrafficAddOn = PluginInstall.State.NotInstalled, CslPackages = 13, CslNames = "BLUEBELL", FsltlFolder = @"C:\MSFS\Community\fsltl-traffic-base",
            FsltlHealth = new(3350, 98, 0, "1.5.0"), BatcModels = BatcTrafficModels.Outcome.Found, BatcCommunity = @"C:\MSFS\Community",
        };
        var window = new PanelWindow(() => firstRun);
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

        // The same pages once everything is set up and a flight is on: the plugin installed and drawing traffic.
        // The FPLN page shows a SimBrief plan already sent to X-Plane's FMS (fixed times so the picture stays the same).
        var sent = new DateTimeOffset(2026, 10, 5, 7, 40, 0, TimeSpan.Zero);
        var plan = new SimbriefFlight("1", "YBBN", "YSSY", new Uri("https://www.simbrief.com/ofp/flightplans/YBBNYSSY_XPE.fms"))
        {
            Generated = sent.AddMinutes(-12),
            Route = "DCT SANEG H91 ADMAR Q404 ADMOD Y50 BOREE BORE5A",
            CruiseFeet = 43000,
            OriginRunway = "01L",
            DestinationRunway = "34L",
            Star = "BORE5A",
        };
        var flying = Ready with
        {
            TrafficAddOn = PluginInstall.State.Installed, TrafficAircraft = 23, CslPackages = 13, CslNames = "BLUEBELL", FsltlFolder = @"C:\MSFS\Community\fsltl-traffic-base", FsltlLiveries = 3154,
            FsltlHealth = new(3154, 98, 0, "1.5.0"), BatcModels = BatcTrafficModels.Outcome.Found, BatcCommunity = @"C:\MSFS\Community",
            FlightPlan = new FlightPlanView(plan, sent.AddMinutes(-2), SavedAs: plan.FileName, SentAt: sent),
        };
        var cleared = new PushView(new PushClearance(sent.AddMinutes(4), 90, "EAST", "pushback approved. Face east."), Preview: new PushPlan(21, 90, 39, "B"), XPlane: true, BetterPushback: true, Automatic: true, Stage: PushStage.WaitingForBrake, BrakeSet: true);
        window.Close();
        window = new PanelWindow(() => flying);
        // The PUSH page with BeyondATC's clearance; the other pages without, so STATUS shows READY FOR CLEARANCE.
        window.Show();
        foreach (var page in new[] { PanelPage.Status, PanelPage.FlightPlan, PanelPage.Push, PanelPage.Setup, PanelPage.Traffic, PanelPage.Fsltl, PanelPage.StartModels })
        {
            flying = flying with { Push = page == PanelPage.Push ? cleared : null };
            window.ShowPage(page);
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            if (folder is not null)
            {
                frame!.Save(Path.Combine(folder, $"panel-{page.ToString().ToLowerInvariant()}-flying.png"));
            }
        }

        window.Close();
    }

    private static readonly PanelState Converted = Ready with { FsltlFolder = @"C:\MSFS\Community\fsltl-traffic-base", FsltlLiveries = 3112 };

    [Fact]
    public void Models_page_says_when_fsltl_needs_converting_again()
    {
        var row = CduPages.Build(PanelPage.StartModels, Converted with { FsltlOutOfDate = true }).Rows[4];
        Assert.Equal(("UPDATE NEEDED", CduColor.Amber), (row.Left.Text, row.Left.Color));
        Assert.Equal(("UPDATE>", CduKey.ConvertFsltl), (row.Right.Text, row.KeyRight));

        var upToDate = CduPages.Build(PanelPage.StartModels, Converted).Rows[4];
        Assert.Equal(("3112 LIVERIES", CduColor.Green), (upToDate.Left.Text, upToDate.Left.Color));
    }

    [Fact]
    public void Status_mentions_out_of_date_models_in_turn_only_when_nothing_is_more_urgent()
    {
        var turn = TimeSpan.FromSeconds(CduPages.UpdateTurnSeconds);
        var waiting = Converted with { BatcHelper = false, BatcApp = false, FsltlOutOfDate = true };

        Assert.Equal("START BEYONDATC", CduPages.Advice(waiting with { Uptime = turn * 2 }).Text);
        var notice = CduPages.Advice(waiting with { Uptime = turn * 3 });
        Assert.Equal(("FSLTL MODELS OUT OF DATE", CduColor.Amber), (notice.Text, notice.Color));
        Assert.True(notice.Text.Length <= CduPages.Width);

        // With a new release too, the three take turns.
        var update = new UpdateInfo("0.6.0", "https://example.invalid/");
        Assert.Equal(["START BEYONDATC", "NEW VERSION 0.6.0", "FSLTL MODELS OUT OF DATE"],
            Enumerable.Range(0, 3).Select(i => CduPages.Advice(waiting with { Update = update, Uptime = turn * i }).Text));

        // Not over something to fix, not in flight, not while converting.
        Assert.Equal("CLOSE MSFS TO CONNECT", CduPages.Advice(waiting with { MsfsRunning = true, Uptime = turn * 3 }).Text);
        Assert.Equal("READY FOR CLEARANCE", CduPages.Advice(Converted with { FsltlOutOfDate = true, Uptime = turn * 3 }).Text);
        Assert.Equal("START BEYONDATC", CduPages.Advice(waiting with { FsltlConverting = true, Uptime = turn * 3 }).Text);
    }

    [Fact]
    public void Out_of_date_models_have_an_update_key_on_status_and_setup()
    {
        // Ben could not find where to update (2026-10-05): the only UPDATE> was on a welcome page.
        var outOfDate = Converted with { TrafficAddOn = PluginInstall.State.Installed, TrafficAircraft = null, FsltlOutOfDate = true };

        var traffic = CduPages.Build(PanelPage.Status, outOfDate).Rows[4];
        Assert.Equal(("FSLTL MODELS", "UPDATE>", CduColor.Amber, CduKey.GoModels),
            (traffic.LabelRight, traffic.Right.Text, traffic.Right.Color, traffic.KeyRight));

        // While aircraft are drawn the count stays, and the key still opens the models page.
        var flying = CduPages.Build(PanelPage.Status, outOfDate with { TrafficAircraft = 12, PluginModels = 2027 }).Rows[4];
        Assert.Equal(("12 AIRCRAFT", CduKey.GoModels), (flying.Right.Text, flying.KeyRight));

        var setup = CduPages.Build(PanelPage.Setup, outOfDate).Rows[4];
        Assert.Equal(("TRAFFIC", "UPDATE MODELS", CduColor.Amber, "SET UP>", CduKey.GoTraffic),
            (setup.LabelLeft, setup.Left.Text, setup.Left.Color, setup.Right.Text, setup.KeyRight));
        Assert.Contains(CduPages.Build(PanelPage.Traffic, outOfDate).Rows, r => r.Left.Text == "UPDATE NEEDED" && r.KeyRight == CduKey.ConvertFsltl);

        // Up to date, or converting: the plain traffic cell as before.
        Assert.Equal("READY", CduPages.Build(PanelPage.Setup, outOfDate with { FsltlOutOfDate = false }).Rows[4].Left.Text);
        Assert.Equal("WAITING", CduPages.Build(PanelPage.Status, outOfDate with { FsltlConverting = true }).Rows[4].Right.Text);
    }

    [Fact]
    public void Setup_asks_to_close_msfs_only_while_it_runs()
    {
        // A standing "CLOSE MSFS BEFORE FLYING" read as if MSFS had been detected (Ben, 2026-10-05).
        Assert.Equal("", CduPages.Build(PanelPage.Setup, Ready).Scratchpad.Text);
        Assert.Equal("CLOSE MSFS BEFORE FLYING", CduPages.Build(PanelPage.Setup, Ready with { MsfsRunning = true }).Scratchpad.Text);
    }

    [Fact]
    public void Models_page_shows_how_far_a_conversion_is()
    {
        var converting = Converted with { FsltlConverting = true };
        string Shown(Fsltl.ImportProgress? p) => CduPages.Build(PanelPage.StartModels, converting with { FsltlProgress = p }).Rows[4].Left.Text;

        Assert.Equal("CONVERTING...", Shown(null));
        Assert.Equal("MODELS 40/412", Shown(new(Fsltl.ImportStage.Models, 40, 412)));
        Assert.Equal("TEXTURES 1200/3112", Shown(new(Fsltl.ImportStage.Textures, 1200, 3112)));
        Assert.Equal("SAVING...", Shown(new(Fsltl.ImportStage.Saving, 0, 0)));
        Assert.Equal(CduKey.None, CduPages.Build(PanelPage.StartModels, converting).Rows[4].KeyRight);
        Assert.Equal("KEEP THIS WINDOW OPEN", CduPages.Build(PanelPage.StartModels, converting).Scratchpad.Text);
    }

    [Fact]
    public void Models_page_asks_to_close_x_plane_only_while_it_runs()
    {
        var found = Ready with { FsltlFolder = @"C:\MSFS\Community\fsltl-traffic-base" };
        Assert.Equal("", CduPages.Build(PanelPage.StartModels, found).Scratchpad.Text);
        Assert.Equal("CLOSE X-PLANE TO CONVERT", CduPages.Build(PanelPage.StartModels, found with { XPlaneRunning = true }).Scratchpad.Text);
        Assert.Equal("CLOSE X-PLANE TO UPDATE", CduPages.Build(PanelPage.StartModels, Converted with { FsltlOutOfDate = true, XPlaneRunning = true }).Scratchpad.Text);
        Assert.Equal("", CduPages.Build(PanelPage.StartModels, Converted with { XPlaneRunning = true }).Scratchpad.Text);
    }

    [Fact]
    public void Models_page_says_fsltl_comes_from_msfs_when_msfs_is_not_installed()
    {
        var page = CduPages.Build(PanelPage.StartModels, Ready with { MsfsInstalled = false });
        var row = page.Rows[4];
        Assert.Equal("NEEDS MSFS", row.Left.Text);
        Assert.Equal(("CHOOSE>", CduKey.ChooseFsltlFolder), (row.Right.Text, row.KeyRight));
        Assert.Contains(page.Message!, m => m.Text.Contains("No MSFS here", StringComparison.Ordinal));
        Assert.All([page, CduPages.Build(PanelPage.StartModels, Ready)], p => Assert.Contains(p.Message!, m => m.Text.Contains("needs FSLTL", StringComparison.Ordinal)));

        // A copy chosen by hand is just FOUND (not in MSFS).
        Assert.Equal("FOUND", CduPages.Build(PanelPage.StartModels, Ready with { MsfsInstalled = false, FsltlFolder = @"D:\fsltl-traffic-base" }).Rows[4].Left.Text);
    }

    [Fact]
    public void X_plane_settings_page_names_the_ai_aircraft_screen()
    {
        Assert.Contains(CduPages.Build(PanelPage.StartXPlane, Ready).Message!, m => m.Text.Contains("Flight Configuration > AI Aircraft: none", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void Closing_while_converting_asks_for_a_second_close()
    {
        var converting = true;
        var window = new PanelWindow(() => Converted with { FsltlConverting = converting });
        window.Show();

        window.Close();
        Assert.True(window.IsVisible);
        Assert.Equal(PanelPage.StartModels, window.Page);
        Assert.Equal(PanelWindow.CloseAgainText, window.Shown!.Scratchpad.Text);

        window.Close();
        Assert.False(window.IsVisible);

        // Not converting: closes at once.
        converting = false;
        window = new PanelWindow(() => Converted with { FsltlConverting = converting });
        window.Show();
        window.Close();
        Assert.False(window.IsVisible);
    }
}

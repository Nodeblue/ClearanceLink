using System.Net;
using ClearanceLink.Host.Panel;
using ClearanceLink.Host.Support;

namespace ClearanceLink.Host.Tests;

/// <summary>The latest SimBrief plan saved as an X-Plane .fms, to load in the FMS by name.</summary>
public sealed class SimbriefPlanTests : IDisposable
{
    // Shape of SimBrief's xml.fetcher JSON as read on Ben's PC (2026-10-05); values made up.
    private const string Latest = """
        {
          "fetch": { "status": "Success" },
          "params": { "request_id": "123456789", "time_generated": "1790840770", "airac": "2610" },
          "general": { "route": "DCT SANEG H91 ADMAR Q404 ADMOD Y50 BOREE BORE5A", "initial_altitude": "43000", "sid_ident": {}, "star_ident": "BORE5A" },
          "origin": { "icao_code": "YBBN", "plan_rwy": "01L" },
          "destination": { "icao_code": "YSSY", "plan_rwy": "34L" },
          "fms_downloads": {
            "directory": "https://www.simbrief.com/ofp/flightplans/",
            "xpe": { "name": "X-Plane 11/12", "link": "YBBNYSSY_XPE_1790840770.fms" },
            "xpn": { "name": "X-Plane 11/12 (no SID/STAR)", "link": "YBBNYSSY_XPN_1790840770.fms" }
          }
        }
        """;

    private const string Fms = "I\n1100 Version\nCYCLE 2610\nADEP YBBN\nDEPRWY RW01L\nADES YSSY\nDESRWY RW34L\nSTAR BORE5A\nNUMENR 3\n";

    private readonly string _root = Directory.CreateTempSubdirectory("clink-simbrief-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Reads_the_x_plane_download_from_the_latest_plan()
    {
        var flight = SimbriefPlan.FromJson(Latest)!;
        Assert.Equal(("123456789", "YBBN", "YSSY"), (flight.RequestId, flight.Origin, flight.Destination));
        Assert.Equal("https://www.simbrief.com/ofp/flightplans/YBBNYSSY_XPE_1790840770.fms", flight.FmsFile.ToString());
        Assert.Equal("YBBNYSSY.fms", flight.FileName);
        Assert.Equal(("01L", null, "BORE5A", "34L", 43000), (flight.OriginRunway, flight.Sid, flight.Star, flight.DestinationRunway, flight.CruiseFeet));
        Assert.Equal("DCT SANEG H91 ADMAR Q404 ADMOD Y50 BOREE BORE5A", flight.Route);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790840770), flight.Generated);
    }

    [Theory]
    [InlineData("""{ "fetch": { "status": "Error: Unknown UserID" } }""")]
    [InlineData("""{ "fetch": { "status": "Success" }, "params": { "request_id": "1" }, "origin": { "icao_code": "YBBN" }, "destination": { "icao_code": "YSSY" } }""")]
    [InlineData("""{ "params": { "request_id": "1" }, "origin": { "icao_code": "YBBN" }, "destination": { "icao_code": "YSSY" }, "fms_downloads": { "directory": "https://x/", "xpe": { "link": "../../evil.fms" } } }""")]
    [InlineData("""{ "params": { "request_id": "1" }, "origin": { "icao_code": "../x" }, "destination": { "icao_code": "YSSY" }, "fms_downloads": { "directory": "https://x/", "xpe": { "link": "a.fms" } } }""")]
    [InlineData("""{ "params": { "request_id": "1" }, "origin": { "icao_code": "YBBN" }, "destination": { "icao_code": "YSSY" }, "fms_downloads": { "directory": "http://x/", "xpe": { "link": "a.fms" } } }""")]
    public void No_plan_when_simbrief_has_none_or_it_looks_wrong(string json) => Assert.Null(SimbriefPlan.FromJson(json));

    [Fact]
    public void A_numeric_id_is_a_user_id_and_anything_else_a_username()
    {
        Assert.Contains("?userid=480000&", SimbriefPlan.LatestUri("480000").ToString(), StringComparison.Ordinal);
        Assert.Contains("?username=ben_flies&", SimbriefPlan.LatestUri("ben_flies").ToString(), StringComparison.Ordinal);
        Assert.Equal("42", SimbriefPlan.PilotId(new AppSettings { SimbriefId = "42" }));
    }

    [Fact]
    public async Task Request_only_shows_the_plan_and_load_saves_it_and_sends_it_to_the_fms()
    {
        var web = new Web(Latest, Fms);
        var sent = new List<string>();
        var plans = new FlightPlans(() => new AppSettings { SimbriefId = "480000" }, () => _root, p => { sent.Add(p); return true; }, web);
        Assert.Equal("REQUEST A PLAN FIRST", plans.Load());

        Assert.Equal("PLAN RECEIVED", await plans.RequestAsync());
        Assert.Equal("YBBN", plans.View.Plan!.Origin);
        Assert.False(Directory.Exists(Path.Combine(_root, "Output"))); // nothing in X-Plane yet
        Assert.Equal("NO NEWER PLAN", await plans.RequestAsync());

        Assert.Equal("SENT TO FMS: CHECK IT", plans.Load());
        var file = Path.Combine(_root, "Output", "FMS plans", "YBBNYSSY.fms");
        Assert.Equal([file], sent);
        Assert.Equal(Fms, await File.ReadAllTextAsync(file));
        Assert.NotNull(plans.View.SentAt);

        plans.FmsAnswered("this X-Plane cannot load flight plans");
        Assert.Equal(("this X-Plane cannot load flight plans", null), (plans.View.FmsProblem, plans.View.SentAt));
    }

    [Fact]
    public async Task Without_the_plugin_the_plan_is_saved_for_the_fms_menu()
    {
        var plans = new FlightPlans(() => new AppSettings { SimbriefId = "480000" }, () => _root, _ => false, new Web(Latest, Fms));
        await plans.RequestAsync();
        Assert.Equal("SAVED: LOAD YBBNYSSY IN FMS", plans.Load());
        Assert.Equal("YBBNYSSY.fms", plans.View.SavedAs);
    }

    [Fact]
    public async Task Problems_are_said_on_the_scratchpad_and_nothing_is_saved()
    {
        // No ID set and no ATC app chosen to take one from.
        Assert.Equal("NO SIMBRIEF PILOT ID", await new FlightPlans(() => new AppSettings(), () => _root, _ => true, new Web(Latest, Fms)).RequestAsync());

        var html = new FlightPlans(() => new AppSettings { SimbriefId = "1" }, () => _root, _ => true, new Web(Latest, "<html>Not found</html>"));
        Assert.Equal("PLAN NOT DOWNLOADED", await html.RequestAsync());
        Assert.Equal("REQUEST A PLAN FIRST", html.Load());
        var error = new FlightPlans(() => new AppSettings { SimbriefId = "1" }, () => _root, _ => true, new Web("""{ "fetch": { "status": "Error" } }""", Fms));
        Assert.Equal("NO PLAN FROM SIMBRIEF", await error.RequestAsync());
        Assert.False(Directory.Exists(Path.Combine(_root, "Output")));
    }

    [Fact]
    public void The_fpln_page_shows_the_plan_asks_before_loading_and_says_what_happened()
    {
        var plan = SimbriefPlan.FromJson(Latest)!;
        var empty = CduPages.Build(PanelPage.FlightPlan, State(new FlightPlanView()));
        Assert.Equal("PRESS REQUEST", empty.Scratchpad.Text);
        Assert.Equal(CduKey.None, empty.Rows[4].KeyRight); // nothing to load

        var received = CduPages.Build(PanelPage.FlightPlan, State(new FlightPlanView(plan, DateTimeOffset.UtcNow)));
        Assert.Equal(("YBBN-YSSY", "FL430"), (received.Rows[0].Left.Text, received.Rows[0].Right.Text));
        Assert.Equal(("01L/-", "BORE5A/34L"), (received.Rows[1].Left.Text, received.Rows[1].Right.Text));
        Assert.StartsWith("SANEG H91", received.Rows[2].Left.Text, StringComparison.Ordinal);
        Assert.True(received.Rows[2].Left.Text.Length <= CduPages.Width);
        Assert.Equal(("<REQUEST", CduKey.RequestPlan, "LOAD>", CduKey.LoadPlan),
            (received.Rows[4].Left.Text, received.Rows[4].KeyLeft, received.Rows[4].Right.Text, received.Rows[4].KeyRight));
        Assert.Equal("CHECK, THEN LOAD", received.Scratchpad.Text);

        var armed = CduPages.Build(PanelPage.FlightPlan, State(new FlightPlanView(plan, DateTimeOffset.UtcNow)) with { LoadArmed = true });
        Assert.Equal(("CONFIRM>", CduColor.Amber), (armed.Rows[4].Right.Text, armed.Rows[4].Right.Color));
        Assert.Equal("REPLACES THE FMS PLAN", armed.Scratchpad.Text);

        var sent = CduPages.Build(PanelPage.FlightPlan, State(new FlightPlanView(plan, DateTimeOffset.UtcNow, SavedAs: "YBBNYSSY.fms", SentAt: DateTimeOffset.UtcNow)));
        Assert.StartsWith("SENT ", sent.Rows[3].Right.Text, StringComparison.Ordinal);
        Assert.Equal("CHECK RWY/SID/STAR", sent.Scratchpad.Text);

        var refused = CduPages.Build(PanelPage.FlightPlan, State(new FlightPlanView(plan, DateTimeOffset.UtcNow, SavedAs: "YBBNYSSY.fms", FmsProblem: "needs 12.1")));
        Assert.Equal(("NOT TAKEN", CduColor.Amber), (refused.Rows[3].Right.Text, refused.Rows[3].Right.Color));

        var file = CduPages.Build(PanelPage.FlightPlan, State(new FlightPlanView(plan, DateTimeOffset.UtcNow, SavedAs: "YBBNYSSY.fms")));
        Assert.Equal("LOAD YBBNYSSY IN THE FMS", file.Scratchpad.Text);
        Assert.All(new[] { empty, received, armed, sent, refused, file }.SelectMany(p => p.Rows),
            r => Assert.True(r.Left.Text.Length + r.Right.Text.Length < CduPages.Width + 1));
    }

    [Fact]
    public void Status_says_a_plan_is_ready_on_fpln_until_it_is_loaded()
    {
        var turn = TimeSpan.FromSeconds(CduPages.UpdateTurnSeconds);
        var plan = new FlightPlanView(SimbriefPlan.FromJson(Latest), DateTimeOffset.UtcNow);
        Assert.Equal(["START ATC APP", "FLIGHT PLAN READY: FPLN"],
            Enumerable.Range(0, 2).Select(i => CduPages.Advice(State(plan) with { Uptime = turn * i }).Text));
        Assert.Equal("START ATC APP", CduPages.Advice(State(plan with { SavedAs = "YBBNYSSY.fms" }) with { Uptime = turn }).Text);
        Assert.Equal((CduKey.GoFlightPlan, "FPLN>"), (CduPages.Build(PanelPage.Status, State(plan)).Rows[5].KeyRight, CduPages.Build(PanelPage.Status, State(plan)).Rows[5].Right.Text));
    }

    private static PanelState State(FlightPlanView plan) => new(
        "0.5.1", true, true, "12.4.3", 0, false, null, null, false, ConnectionMode.Automatic, false, true, 0, 7, TimeSpan.Zero, @"C:\Tower\Tower.exe", null,
        FlightPlan: plan);

    private static async Task WaitFor(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++)
        {
            await Task.Delay(25);
        }

        Assert.True(done());
    }

    private sealed class Web(string latest, string fms) : HttpMessageHandler
    {
        private int _plans;

        public int Plans => Volatile.Read(ref _plans);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var isPlan = request.RequestUri!.Host == "www.simbrief.com" && request.RequestUri.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal);
            if (isPlan)
            {
                Interlocked.Increment(ref _plans);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(isPlan ? latest : fms) });
        }
    }
}

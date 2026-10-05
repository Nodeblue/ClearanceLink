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
    public async Task Saves_the_plan_into_output_fms_plans_and_only_again_when_simbrief_has_a_new_one()
    {
        var web = new Web(Latest, Fms);
        var saved = new List<string>();
        using var cts = new CancellationTokenSource();
        var run = SimbriefPlan.FollowAsync(() => new AppSettings { SimbriefId = "480000" }, () => _root, f => { lock (saved) saved.Add(f); }, web,
            TimeSpan.FromMilliseconds(50), cts.Token);
        await WaitFor(() => web.Plans >= 4);
        await cts.CancelAsync();
        await run;

        Assert.Equal(["YBBNYSSY.fms"], saved); // asked four times, saved once
        Assert.Equal(Fms, await File.ReadAllTextAsync(Path.Combine(_root, "Output", "FMS plans", "YBBNYSSY.fms")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "Output", "FMS plans"), "*.tmp"));
    }

    [Fact]
    public async Task Nothing_is_saved_when_the_download_is_not_a_flight_plan_or_the_setting_is_off()
    {
        var flight = SimbriefPlan.FromJson(Latest)!;
        Assert.Null(await SimbriefPlan.SaveAsync(flight, _root, new Web(Latest, "<html>Not found</html>")));
        Assert.False(Directory.Exists(Path.Combine(_root, "Output")));

        var web = new Web(Latest, Fms);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await SimbriefPlan.FollowAsync(() => new AppSettings { SimbriefId = "480000", SimbriefPlans = false }, () => _root, _ => { }, web,
            TimeSpan.FromMilliseconds(50), cts.Token);
        Assert.Equal(0, web.Plans);
    }

    [Fact]
    public void Status_says_which_plan_was_saved_in_turn()
    {
        var turn = TimeSpan.FromSeconds(CduPages.UpdateTurnSeconds);
        var notes = Enumerable.Range(0, 2).Select(i => CduPages.Advice(new PanelState(
            "0.5.1", true, true, "12.4.3", false, false, 0, null, null, false, ConnectionMode.Automatic, false, true, 0, 7, turn * i, null, null,
            FlightPlan: "YBBNYSSY.fms")).Text).ToList();
        Assert.Equal(["START BEYONDATC", "FMS PLAN YBBNYSSY SAVED"], notes);
    }

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

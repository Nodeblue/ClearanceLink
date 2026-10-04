using XPConnect.Airports;
using XPConnect.Host.Traffic;

namespace XPConnect.Host.Tests;

public sealed class ShowcaseTests
{
    [Fact]
    public void Picks_real_airline_liveries_preferring_local_airlines_and_different_ones()
    {
        var liveries = new List<(string, string, string, string, string)>
        {
            ("FSLTL_B738_ZZZZ", "FSLTL_B738", "B738", "", "B738_W"),
            ("FSLTL_B738_ABC-STUB", "FSLTL_B738_ABC", "B738", "ABC", "B738_W"),
            ("FSLTL_B738_KLM", "FSLTL_B738_KLM", "B738", "KLM", "B738_S"),
            ("FSLTL_B738_Qantas", "FSLTL_B738_QFA", "B738", "QFA", "B738_W"),
            ("FSLTL_A20N_Qantas", "FSLTL_A20N_QFA", "A20N", "QFA", "A20N_CFM"),
            ("FSLTL_A20N_Jetstar", "FSLTL_A20N_JST", "A20N", "JST", "A20N_PW"),
        };

        var picked = Showcase.Pick(liveries, 9);

        Assert.Equal(
            [new ShowcaseAircraft("Narrow body", "B738", "QFA", "FSLTL_B738_QFA", 40, "B738_W"), new ShowcaseAircraft("Narrow body", "A20N", "JST", "FSLTL_A20N_JST", 38, "A20N_PW")],
            picked);

        // The parallel runway: the models not shown yet (scimitar 737, CFM A320neo), each in its own livery.
        var variants = Showcase.PickVariants(liveries, picked.Select(a => a.Model), 3000, 25);
        Assert.Equal([("A20N", "A20N_CFM"), ("B738", "B738_S")], variants.Select(v => (v.IcaoType, v.Model!)).Order());
    }

    [Fact]
    public void Without_imported_liveries_every_type_is_shown_by_type()
    {
        var picked = Showcase.Pick([], 2);

        Assert.Equal(10, picked.Count);  // two of each of the five groups
        Assert.All(picked, a => Assert.Null(a.Livery));
        Assert.Equal(["Light", "Regional jet", "Narrow body", "Wide body", "Heavy"], picked.Select(a => a.Group).Distinct());
    }

    [Fact]
    public void Lines_up_nose_to_tail_with_wider_gaps_between_groups()
    {
        var from = End(-27.40, 153.10);
        var to = End(-27.37, 153.10);  // due north
        ShowcaseAircraft A(string group, double length) => new(group, "X", null, null, length);

        var points = Showcase.LineUp([A("Light", 20), A("Light", 30), A("Heavy", 70)], from, to, gap: 10, startMeters: 100);

        double Along(GroundPoint p) => Geo.DistanceMeters(from.Latitude, from.Longitude, p.Latitude, p.Longitude);
        Assert.Equal(110, Along(points[0]), 0.5);            // start + half its length
        Assert.Equal(110 + 10 + 10 + 15, Along(points[1]), 0.5); // + half, gap, half
        Assert.Equal(145 + 15 + 30 + 35, Along(points[2]), 0.5); // + half, 3 x gap between groups, half
        Assert.All(points, p => Assert.Equal(0, p.Heading, 0.1));
    }

    [Fact]
    public void Taxi_route_is_the_longest_chain_of_the_named_taxiway()
    {
        var airport = new Airport { Icao = "TEST" };
        airport.TaxiNodes.AddRange([Node(1, 0), Node(2, 0.001), Node(3, 0.002), Node(4, 0.003), Node(5, 0.010)]);
        airport.TaxiEdges.AddRange(
        [
            new TaxiEdge(1, 2, false, false, "E", "A"),
            new TaxiEdge(2, 3, false, false, "E", "A"),
            new TaxiEdge(3, 4, false, false, "E", "A"),
            new TaxiEdge(4, 5, false, false, "E", "B"),
        ]);

        var (name, route) = Showcase.TaxiRoute(airport, "A");

        Assert.Equal("A", name);
        Assert.Equal(4, route.Count);
        Assert.Equal("B", Showcase.TaxiRoute(airport, null).Name);  // any taxiway: the longest
    }

    [Fact]
    public void Taxiing_goes_out_and_back_facing_the_way_it_moves()
    {
        List<(double, double)> route = [(-27.40, 153.10), (-27.39, 153.10)];  // about 1112 m due north
        var length = Showcase.Length(route);

        var outbound = Showcase.Along(route, 100);
        var inbound = Showcase.Along(route, length + 100);

        Assert.Equal(0, outbound.Heading, 1);
        Assert.Equal(180, inbound.Heading, 1);
        Assert.Equal(outbound.Latitude + (route[1].Item1 - route[0].Item1) * (length - 200) / length, inbound.Latitude, 6);
    }

    private static RunwayEnd End(double lat, double lon) =>
        new("01", lat, lon, 0, 0, 0, 0, false, 0);

    private static TaxiNode Node(int id, double north) => new(id, -27.40 + north, 153.10, "both", "");
}

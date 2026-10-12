using System.Globalization;

namespace ClearanceLink.Airports.Tests;

/// <summary>
/// Synthetic X-Plane taxi networks laid out in metres that used to give two taxi points one position (the app's 1.8.21
/// crashed on coincident points; MSFS keeps lead-in points 5.6 m or more from their spots).
/// </summary>
public sealed class TaxiNetworkSpacingTests
{
    private static readonly LocalFrame Frame = new(-27.4, 153.1);

    [Fact]
    public void Many_stands_projecting_onto_one_short_stub_get_separate_points()
    {
        // A 10.5 m stub only has room for a split at its middle; the 1 m clamp used to stack the sixth and later
        // stands on the same point near the stub's end.
        var stands = Enumerable.Range(0, 7).Select(k => (5.25, 20.0 + (10 * k))).ToArray();
        var network = Build([(0, 0), (10.5, 0)], [(0, 1)], stands);

        AssertSpaced(network, stands.Length);
    }

    [Fact]
    public void Back_spurs_and_split_points_on_one_segment_do_not_coincide()
    {
        // Stands within 10 m of node 0 get back-spurs 10 m and 12 m along the segment; stands further out project
        // onto the segment exactly there and split it at the same positions.
        (double, double)[] stands = [(2, 6), (-3, 5), (10, 30), (12, 40)];
        var network = Build([(0, 0), (40, 0)], [(0, 1)], stands);

        AssertSpaced(network, stands.Length);
    }

    [Fact]
    public void Coincident_x_plane_nodes_are_pulled_apart_along_their_paths()
    {
        // Two X-Plane nodes 0.2 m apart on different taxiways (hand-made scenery does this).
        var network = Build([(0, 0), (50, 0), (50.2, 0), (100, 0)], [(0, 1), (2, 3)], [(25, 30)]);

        AssertSpaced(network, 1);
    }

    private static TaxiNetwork Build((double X, double Z)[] nodes, (int From, int To)[] edges, (double X, double Z)[] stands)
    {
        var lines = new List<string>
        {
            "I",
            "1200 Version",
            "1 13 0 0 YTST Test",
            $"100 30.00 1 0 0.25 1 2 1 01 {Lat(0, -800)} 0 0 3 5 1 1 19 {Lat(0, -400)} 0 0 3 0 0 0",
            $"1302 datum_lat {Frame.Latitude.ToString(CultureInfo.InvariantCulture)}",
            $"1302 datum_lon {Frame.Longitude.ToString(CultureInfo.InvariantCulture)}",
        };
        lines.AddRange(stands.Select((s, i) => $"1300 {Lat(s.X, s.Z)} 180.00 gate jets Gate {i + 1}"));
        lines.AddRange(nodes.Select((n, i) => $"1201 {Lat(n.X, n.Z)} both {i}"));
        lines.AddRange(edges.Select(e => $"1202 {e.From} {e.To} twoway taxiway_E A"));
        lines.Add("99");
        var airport = Assert.Single(AptDatParser.Parse(new StringReader(string.Join("\n", lines))));
        var network = TaxiNetwork.Build(airport);
        Assert.False(network.Generated);
        return network;
    }

    private static string Lat(double x, double z)
    {
        var (lat, lon) = Frame.FromLocal(x, z);
        return string.Create(CultureInfo.InvariantCulture, $"{lat:0.000000000} {lon:0.000000000}");
    }

    private static void AssertSpaced(TaxiNetwork network, int stands)
    {
        Assert.Equal(stands, network.Parking.Count);
        var points = network.Points;
        var closest = double.MaxValue;
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                var d = Geo.DistanceMeters(points[i].Latitude, points[i].Longitude, points[j].Latitude, points[j].Longitude);
                Assert.True(d >= 0.5, $"points {i} and {j} are {d:0.00} m apart");
                closest = Math.Min(closest, d);
            }
        }

        Assert.True(closest >= 0.99, $"closest points {closest:0.00} m apart");

        // Every spot keeps one lead-in point of its own, clear of the spot, and no taxi point sits on any spot.
        var parkingPaths = network.Paths.Where(p => p.Type == MsfsCodes.ParkingPath).ToList();
        Assert.Equal(stands, parkingPaths.Select(p => p.Start).Distinct().Count());
        foreach (var path in parkingPaths)
        {
            var spot = network.Parking[path.End].Start;
            Assert.True(Geo.DistanceMeters(points[path.Start].Latitude, points[path.Start].Longitude, spot.Latitude, spot.Longitude) >= 4.99, "lead-in point on its spot");
        }

        foreach (var spot in network.Parking)
        {
            Assert.All(points, p => Assert.True(Geo.DistanceMeters(p.Latitude, p.Longitude, spot.Start.Latitude, spot.Start.Longitude) >= 4.99, "taxi point on a spot"));
        }
    }
}

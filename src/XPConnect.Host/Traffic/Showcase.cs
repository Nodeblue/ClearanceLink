using XPConnect.Airports;

namespace XPConnect.Host.Traffic;

/// <summary>One aircraft the showcase draws: type, airline and (imported FSLTL) livery key, and its size class.</summary>
public sealed record ShowcaseAircraft(string Group, string IcaoType, string? IcaoAirline, string? Livery, double LengthMeters);

/// <summary>Where an aircraft is drawn: position and true heading.</summary>
public readonly record struct GroundPoint(double Latitude, double Longitude, double Heading);

/// <summary>
/// A repeatable traffic check without BeyondATC (xpconnect showcase): a line-up of aircraft from small to heavy, nose to
/// tail along a runway in size groups, and aircraft taxiing round and round a taxiway, so models, liveries, wheels,
/// propellers and steering can be looked at in one place. Pure geometry here; <see cref="Cli"/> sends it to the plugin.
/// </summary>
public static class Showcase
{
    /// <summary>The size groups in line-up order, with the types tried for each and their rough length in metres.</summary>
    public static readonly IReadOnlyList<(string Group, (string Type, double Length)[] Types)> Groups =
    [
        ("Light", [("C208", 12), ("SF34", 20), ("DH8C", 26), ("AT76", 27), ("DH8D", 33), ("AT45", 23)]),
        ("Regional jet", [("CRJ7", 33), ("E170", 30), ("E190", 36), ("BCS1", 35), ("BCS3", 39)]),
        ("Narrow body", [("B733", 33), ("B737", 31), ("B738", 40), ("B739", 42), ("A319", 34), ("A320", 38), ("A20N", 38), ("A321", 45), ("A21N", 45)]),
        ("Wide body", [("B763", 55), ("B788", 57), ("B789", 63), ("A332", 59), ("A333", 64), ("A359", 67)]),
        ("Heavy", [("B772", 64), ("B77L", 64), ("B77W", 74), ("MD11", 61), ("A346", 75), ("B744", 71), ("B748", 76), ("A388", 73)]),
    ];

    /// <summary>Airlines tried first, in order: Australian ones for Brisbane, then well-known liveries.</summary>
    public static readonly List<string> PreferredAirlines =
        ["QFA", "VOZ", "JST", "QLK", "RXA", "UTY", "ANZ", "SIA", "UAE", "CPA", "QTR", "DAL", "UAL", "AAL", "BAW", "DLH", "AFR", "KLM", "ACA", "JAL", "ANA", "CES", "CSN", "THY"];

    /// <summary>
    /// The aircraft to show, up to <paramref name="perGroup"/> per group. With imported FSLTL liveries
    /// (title, key, type, airline), each type gets a real airline livery, a different airline each time where possible;
    /// without, the plugin picks a CSL model by type.
    /// </summary>
    public static List<ShowcaseAircraft> Pick(IReadOnlyList<(string Title, string Key, string IcaoType, string IcaoAirline)> liveries, int perGroup)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ShowcaseAircraft>();
        foreach (var (group, types) in Groups)
        {
            var picked = 0;
            foreach (var (type, length) in types)
            {
                if (picked >= perGroup) break;
                var candidates = liveries
                    .Where(l => l.IcaoType.Equals(type, StringComparison.OrdinalIgnoreCase) && l.IcaoAirline.Length == 3
                        && !l.IcaoAirline.StartsWith("ZZ", StringComparison.OrdinalIgnoreCase)
                        && !l.Title.Contains("STUB", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(l => used.Contains(l.IcaoAirline) ? 1 : 0)
                    .ThenBy(l => PreferredAirlines.IndexOf(l.IcaoAirline) is var p and >= 0 ? p : int.MaxValue)
                    .ThenBy(l => l.Key, StringComparer.Ordinal)
                    .ToList();
                if (liveries.Count > 0 && candidates.Count == 0) continue;  // FSLTL has none of this type
                var livery = candidates.FirstOrDefault();
                if (livery.Key is not null) used.Add(livery.IcaoAirline);
                result.Add(new ShowcaseAircraft(group, type, livery.Key is null ? null : livery.IcaoAirline, livery.Key, length));
                picked++;
            }
        }

        return result;
    }

    /// <summary>
    /// Places the aircraft nose to tail along a runway from <paramref name="from"/> towards <paramref name="to"/>, on
    /// the centre line, all facing along it: <paramref name="gap"/> metres between aircraft and three times that
    /// between groups.
    /// </summary>
    public static List<GroundPoint> LineUp(IReadOnlyList<ShowcaseAircraft> aircraft, RunwayEnd from, RunwayEnd to, double gap, double startMeters = 150)
    {
        var heading = Geo.BearingDegrees(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
        var along = startMeters;
        var points = new List<GroundPoint>();
        for (var i = 0; i < aircraft.Count; i++)
        {
            if (i > 0) along += (aircraft[i - 1].Group == aircraft[i].Group ? gap : 3 * gap) + aircraft[i - 1].LengthMeters / 2;
            along += aircraft[i].LengthMeters / 2;
            var (lat, lon) = Offset(from.Latitude, from.Longitude, heading, along);
            points.Add(new GroundPoint(lat, lon, heading));
        }

        return points;
    }

    /// <summary>
    /// A taxi route: the longest chain of taxiway segments named <paramref name="taxiway"/> (any name when null, then
    /// the longest chain of same-named segments), as points from one end to the other.
    /// </summary>
    public static (string Name, List<(double Latitude, double Longitude)> Points) TaxiRoute(Airport airport, string? taxiway)
    {
        var nodes = airport.TaxiNodes.ToDictionary(n => n.Id);
        var names = taxiway is null
            ? airport.TaxiEdges.Where(e => !e.IsRunway && e.Name.Length > 0).Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [taxiway];
        List<(double, double)> best = [];
        var bestName = "";
        var bestLength = 0.0;
        foreach (var name in names)
        {
            var edges = airport.TaxiEdges.Where(e => !e.IsRunway && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && nodes.ContainsKey(e.From) && nodes.ContainsKey(e.To)).ToList();
            var route = LongestChain(edges, nodes);
            var length = Length(route);
            if (length > bestLength)
            {
                best = route;
                bestName = name;
                bestLength = length;
            }
        }

        return (bestName, best);
    }

    /// <summary>
    /// Where an aircraft is <paramref name="distance"/> metres along a back-and-forth trip over <paramref name="route"/>
    /// (out to the far end, turn round, back): its heading looks <paramref name="lookAhead"/> metres ahead, so it turns
    /// smoothly through corners and round at the ends.
    /// </summary>
    public static GroundPoint Along(IReadOnlyList<(double Latitude, double Longitude)> route, double distance, double lookAhead = 25)
    {
        var length = Length(route);
        if (route.Count == 0) return default;
        if (route.Count < 2 || length <= 0) return new GroundPoint(route[0].Latitude, route[0].Longitude, 0);
        var here = PointOnLoop(route, length, distance);
        var ahead = PointOnLoop(route, length, distance + lookAhead);
        var heading = Geo.BearingDegrees(here.Latitude, here.Longitude, ahead.Latitude, ahead.Longitude);
        return new GroundPoint(here.Latitude, here.Longitude, heading);
    }

    public static double Length(IReadOnlyList<(double Latitude, double Longitude)> route)
    {
        var total = 0.0;
        for (var i = 1; i < route.Count; i++) total += Geo.DistanceMeters(route[i - 1].Latitude, route[i - 1].Longitude, route[i].Latitude, route[i].Longitude);
        return total;
    }

    /// <summary>The point <paramref name="distance"/> metres along the route and back (a loop twice its length).</summary>
    private static (double Latitude, double Longitude) PointOnLoop(IReadOnlyList<(double Latitude, double Longitude)> route, double length, double distance)
    {
        var d = distance % (2 * length);
        if (d < 0) d += 2 * length;
        if (d > length) d = 2 * length - d;  // on the way back
        for (var i = 1; i < route.Count; i++)
        {
            var (aLat, aLon) = route[i - 1];
            var (bLat, bLon) = route[i];
            var segment = Geo.DistanceMeters(aLat, aLon, bLat, bLon);
            if (d <= segment || i == route.Count - 1)
            {
                var f = segment <= 0 ? 0 : Math.Clamp(d / segment, 0, 1);
                return (aLat + (bLat - aLat) * f, aLon + (bLon - aLon) * f);
            }

            d -= segment;
        }

        return route[^1];
    }

    /// <summary>The longest simple path through the edges (they form a taxiway's chain, usually a line).</summary>
    private static List<(double, double)> LongestChain(List<TaxiEdge> edges, Dictionary<int, TaxiNode> nodes)
    {
        var links = new Dictionary<int, List<int>>();
        foreach (var e in edges)
        {
            (links.TryGetValue(e.From, out var a) ? a : links[e.From] = []).Add(e.To);
            (links.TryGetValue(e.To, out var b) ? b : links[e.To] = []).Add(e.From);
        }

        double Dist(int a, int b) => Geo.DistanceMeters(nodes[a].Latitude, nodes[a].Longitude, nodes[b].Latitude, nodes[b].Longitude);

        // Farthest node from any end, then farthest from that (exact on a tree, good enough on a taxiway).
        (int Node, double Distance, Dictionary<int, int> Previous) Farthest(int start)
        {
            var distance = new Dictionary<int, double> { [start] = 0 };
            var previous = new Dictionary<int, int>();
            var queue = new PriorityQueue<int, double>();
            queue.Enqueue(start, 0);
            while (queue.TryDequeue(out var n, out var dn))
            {
                if (dn > distance[n]) continue;
                foreach (var m in links[n])
                {
                    var dm = dn + Dist(n, m);
                    if (distance.TryGetValue(m, out var known) && known <= dm) continue;
                    distance[m] = dm;
                    previous[m] = n;
                    queue.Enqueue(m, dm);
                }
            }

            var far = distance.MaxBy(kv => kv.Value);
            return (far.Key, far.Value, previous);
        }

        List<(double, double)> best = [];
        var bestLength = 0.0;
        var seen = new HashSet<int>();
        foreach (var start in links.Keys)
        {
            if (!seen.Add(start)) continue;
            var (end1, _, _) = Farthest(start);
            var (end2, length, previous) = Farthest(end1);
            foreach (var n in previous.Keys) seen.Add(n);
            if (length <= bestLength) continue;
            var path = new List<int> { end2 };
            while (previous.TryGetValue(path[^1], out var p)) path.Add(p);
            best = [.. path.Select(n => (nodes[n].Latitude, nodes[n].Longitude))];
            bestLength = length;
        }

        return best;
    }

    /// <summary>The point <paramref name="meters"/> from a position along a true bearing (flat earth; fine over an airport).</summary>
    public static (double Latitude, double Longitude) Offset(double lat, double lon, double bearing, double meters)
    {
        const double MetersPerDegree = 111_320;
        var b = bearing * Math.PI / 180;
        var dLat = meters * Math.Cos(b) / MetersPerDegree;
        var dLon = meters * Math.Sin(b) / (MetersPerDegree * Math.Cos(lat * Math.PI / 180));
        return (lat + dLat, lon + dLon);
    }
}

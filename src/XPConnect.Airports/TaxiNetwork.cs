namespace XPConnect.Airports;

/// <summary>A taxi network point in MSFS terms. Type is 1 (normal) or 5 (hold short).</summary>
public sealed record TaxiPoint(double Latitude, double Longitude, int Type);

/// <summary>
/// A taxi network path in MSFS terms. For parking paths (type 3) Start is a taxi point and End a parking index,
/// as MSFS sends them; otherwise both are taxi point indexes.
/// </summary>
public sealed record TaxiPath(int Type, int Start, int End, int NameIndex, double Width, int RunwayNumber = 0, int RunwayDesignator = 0);

/// <summary>A parking spot with its MSFS type, name, number and suffix already worked out.</summary>
public sealed record ParkingSpot(StartLocation Start, int Type, int Name, int Number, int Suffix, double Radius)
{
    public double Heading => ((Start.Heading % 360) + 360) % 360;
}

/// <summary>
/// The taxi network an ATC client sees: X-Plane's 1201/1202 network with hold-short points marked and every
/// parking spot joined to it, or, for airports without one, a simple generated network (see <see cref="Generated"/>).
/// </summary>
public sealed class TaxiNetwork
{
    private const double SnapMeters = 8;

    public List<TaxiPoint> Points { get; } = [];

    public List<TaxiPath> Paths { get; } = [];

    /// <summary>Taxiway names. Index 0 is the empty name, as in MSFS.</summary>
    public List<string> Names { get; } = [""];

    public List<ParkingSpot> Parking { get; } = [];

    /// <summary>True when X-Plane had no taxi network here and this one was generated from runways and parking.</summary>
    public bool Generated { get; private set; }

    public static TaxiNetwork Build(Airport airport)
    {
        var network = new TaxiNetwork();
        network.AddParking(airport);
        var frame = new LocalFrame(airport.ReferencePoint.Latitude, airport.ReferencePoint.Longitude);
        if (airport.TaxiEdges.Count > 0 && airport.TaxiNodes.Count > 0)
        {
            network.FromXPlane(airport, frame);
        }
        else
        {
            network.Generate(airport, frame);
        }

        return network;
    }

    private void AddParking(Airport airport)
    {
        var used = new HashSet<(int, int, int)>();
        var pending = new List<(StartLocation Start, int Name, int Suffix)>();
        foreach (var start in airport.StartLocations)
        {
            var (name, number, suffix) = MsfsCodes.Parking(start.Name, start.Type == "gate");
            if (number < 0 || !used.Add((name, number, suffix)))
            {
                pending.Add((start, name, suffix));
                Parking.Add(null!);
                continue;
            }

            Parking.Add(Spot(start, name, number, suffix));
        }

        // Unnumbered or duplicate names get the next free number of their kind, so every spot stays distinct.
        var next = 0;
        for (var i = 0; i < Parking.Count; i++)
        {
            if (Parking[i] is not null)
            {
                continue;
            }

            var (start, name, suffix) = pending[next++];
            var number = 1;
            while (!used.Add((name, number, suffix)))
            {
                number++;
            }

            Parking[i] = Spot(start, name, number, suffix);
        }
    }

    private static ParkingSpot Spot(StartLocation start, int name, int number, int suffix) =>
        new(start, MsfsCodes.ParkingType(start), name, number, suffix, MsfsCodes.ParkingRadius(start));

    private int NameIndex(string name)
    {
        name = name.Trim();
        var index = Names.IndexOf(name);
        if (index < 0)
        {
            Names.Add(name);
            index = Names.Count - 1;
        }

        return index;
    }

    private void FromXPlane(Airport airport, LocalFrame frame)
    {
        var nodes = airport.TaxiNodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
        var edges = airport.TaxiEdges.Where(e => nodes.ContainsKey(e.From) && nodes.ContainsKey(e.To) && e.From != e.To).ToList();

        var index = new Dictionary<int, int>();
        foreach (var id in edges.SelectMany(e => new[] { e.From, e.To }).Distinct().Order())
        {
            index[id] = Points.Count;
            Points.Add(new TaxiPoint(nodes[id].Latitude, nodes[id].Longitude, MsfsCodes.NormalPoint));
        }

        foreach (var hold in HoldShortNodes(edges))
        {
            Points[index[hold]] = Points[index[hold]] with { Type = MsfsCodes.HoldShortPoint };
        }

        // Names are listed in sorted order, as MSFS does, so build the list before the paths.
        foreach (var name in edges.Where(e => !e.IsRunway).Select(e => e.Name.Trim()).Where(n => n.Length > 0).Distinct().Order(StringComparer.Ordinal))
        {
            NameIndex(name);
        }

        foreach (var edge in edges)
        {
            if (edge.IsRunway)
            {
                var runway = FindRunway(airport, edge.Name, nodes[edge.From]);
                var (number, designator) = runway is null ? (0, 0) : Primary(runway);
                Paths.Add(new TaxiPath(MsfsCodes.RunwayPath, index[edge.From], index[edge.To], 0, runway?.WidthMeters ?? 45, number, designator));
            }
            else
            {
                Paths.Add(new TaxiPath(MsfsCodes.TaxiwayPath, index[edge.From], index[edge.To], NameIndex(edge.Name), MsfsCodes.TaxiwayWidth(edge.WidthClass)));
            }
        }

        ConnectParking(frame);
    }

    /// <summary>
    /// Hold-short points: where a taxiway leaves a runway's protected zone (a node joining a 1204-flagged taxiway edge
    /// to an unflagged one). Airports without 1204 rows get the first taxiway node off each runway instead.
    /// </summary>
    private static HashSet<int> HoldShortNodes(List<TaxiEdge> edges)
    {
        var onRunway = edges.Where(e => e.IsRunway).SelectMany(e => new[] { e.From, e.To }).ToHashSet();
        var taxiways = edges.Where(e => !e.IsRunway).ToList();
        var holds = new HashSet<int>();
        if (taxiways.Any(e => e.ActiveZones.Count > 0))
        {
            var zoned = new HashSet<int>();
            var plain = new HashSet<int>();
            foreach (var edge in taxiways)
            {
                var set = edge.ActiveZones.Count > 0 ? zoned : plain;
                set.Add(edge.From);
                set.Add(edge.To);
            }

            holds.UnionWith(zoned.Where(n => plain.Contains(n) && !onRunway.Contains(n)));
            return holds;
        }

        foreach (var edge in taxiways)
        {
            if (onRunway.Contains(edge.From) && !onRunway.Contains(edge.To))
            {
                holds.Add(edge.To);
            }
            else if (onRunway.Contains(edge.To) && !onRunway.Contains(edge.From))
            {
                holds.Add(edge.From);
            }
        }

        return holds;
    }

    private static Runway? FindRunway(Airport airport, string edgeName, TaxiNode near)
    {
        var ends = edgeName.Split('/', StringSplitOptions.TrimEntries);
        var named = airport.Runways.FirstOrDefault(r => ends.Contains(r.End1.Number) && ends.Contains(r.End2.Number));
        return named ?? airport.Runways
            .OrderBy(r => Geo.DistanceMeters(near.Latitude, near.Longitude, (r.End1.Latitude + r.End2.Latitude) / 2, (r.End1.Longitude + r.End2.Longitude) / 2))
            .FirstOrDefault();
    }

    /// <summary>MSFS makes the higher-numbered end the runway's primary end (19 of 01/19, 29 of 11/29).</summary>
    public static (int Number, int Designator) Primary(Runway runway)
    {
        var a = MsfsCodes.Runway(runway.End1.Number);
        var b = MsfsCodes.Runway(runway.End2.Number);
        return a.Number >= b.Number ? a : b;
    }

    public static bool End1IsPrimary(Runway runway) =>
        MsfsCodes.Runway(runway.End1.Number).Number >= MsfsCodes.Runway(runway.End2.Number).Number;

    /// <summary>
    /// Joins each parking spot to the nearest taxiway: to an existing point when one is within a few metres of the
    /// nearest spot on the taxiway, otherwise to a new point that splits that taxiway path.
    /// </summary>
    private void ConnectParking(LocalFrame frame)
    {
        var local = Points.Select(p => frame.ToLocal(p.Latitude, p.Longitude)).ToList();
        var candidates = Enumerable.Range(0, Paths.Count).Where(i => Paths[i].Type == MsfsCodes.TaxiwayPath).ToList();
        if (candidates.Count == 0)
        {
            candidates = Enumerable.Range(0, Paths.Count).ToList();
        }

        var splits = new Dictionary<int, List<(double T, int Parking)>>();
        var direct = new List<(int Point, int Parking)>();
        for (var p = 0; p < Parking.Count; p++)
        {
            var spot = frame.ToLocal(Parking[p].Start.Latitude, Parking[p].Start.Longitude);
            var best = (Path: -1, T: 0.0, Distance: double.MaxValue);
            foreach (var i in candidates)
            {
                var (t, distance) = Project(spot, local[Paths[i].Start], local[Paths[i].End]);
                if (distance < best.Distance)
                {
                    best = (i, t, distance);
                }
            }

            if (best.Path < 0)
            {
                continue;
            }

            var path = Paths[best.Path];
            var length = Distance(local[path.Start], local[path.End]);
            if (best.T * length < SnapMeters)
            {
                direct.Add((path.Start, p));
            }
            else if ((1 - best.T) * length < SnapMeters)
            {
                direct.Add((path.End, p));
            }
            else
            {
                if (!splits.TryGetValue(best.Path, out var list))
                {
                    splits[best.Path] = list = [];
                }

                list.Add((best.T, p));
            }
        }

        foreach (var (pathIndex, list) in splits)
        {
            var path = Paths[pathIndex];
            var a = local[path.Start];
            var b = local[path.End];
            var length = Distance(a, b);
            var chain = new List<int> { path.Start };
            var lastT = 0.0;
            foreach (var (t, parking) in list.OrderBy(s => s.T))
            {
                if ((t - lastT) * length >= SnapMeters || chain.Count == 1)
                {
                    var (lat, lon) = frame.FromLocal(a.X + ((b.X - a.X) * t), a.Z + ((b.Z - a.Z) * t));
                    Points.Add(new TaxiPoint(lat, lon, MsfsCodes.NormalPoint));
                    local.Add(frame.ToLocal(lat, lon));
                    chain.Add(Points.Count - 1);
                    lastT = t;
                }

                direct.Add((chain[^1], parking));
            }

            chain.Add(path.End);
            Paths[pathIndex] = path with { End = chain[1] };
            for (var i = 1; i < chain.Count - 1; i++)
            {
                Paths.Add(path with { Start = chain[i], End = chain[i + 1] });
            }
        }

        foreach (var (point, parking) in direct.OrderBy(d => d.Parking))
        {
            Paths.Add(new TaxiPath(MsfsCodes.ParkingPath, point, parking, 0, 10));
        }
    }

    private static (double T, double Distance) Project((double X, double Z) p, (double X, double Z) a, (double X, double Z) b)
    {
        var dx = b.X - a.X;
        var dz = b.Z - a.Z;
        var lengthSquared = (dx * dx) + (dz * dz);
        var t = lengthSquared == 0 ? 0 : Math.Clamp((((p.X - a.X) * dx) + ((p.Z - a.Z) * dz)) / lengthSquared, 0, 1);
        return (t, Distance(p, (a.X + (t * dx), a.Z + (t * dz))));
    }

    private static double Distance((double X, double Z) a, (double X, double Z) b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    /// <summary>
    /// A stand-in network for airports X-Plane has no taxi routes for: each runway as runway paths, a hold-short
    /// point beside each runway end on the parking side, and one apron point that every hold-short point and
    /// parking spot joins. Enough for a client to route parking to runway and back; the routes carry no names.
    /// </summary>
    private void Generate(Airport airport, LocalFrame frame)
    {
        Generated = true;
        var hub = Parking.Count > 0
            ? frame.ToLocal(Parking.Average(p => p.Start.Latitude), Parking.Average(p => p.Start.Longitude))
            : (X: 0.0, Z: 0.0);
        var hubPoint = Add(frame, hub, MsfsCodes.NormalPoint);

        foreach (var runway in airport.Runways)
        {
            var (number, designator) = Primary(runway);
            var a = frame.ToLocal(runway.End1.Latitude, runway.End1.Longitude);
            var b = frame.ToLocal(runway.End2.Latitude, runway.End2.Longitude);
            var middle = ((a.X + b.X) / 2, (a.Z + b.Z) / 2);
            var end1 = Add(frame, a, MsfsCodes.NormalPoint);
            var centre = Add(frame, middle, MsfsCodes.NormalPoint);
            var end2 = Add(frame, b, MsfsCodes.NormalPoint);
            Paths.Add(new TaxiPath(MsfsCodes.RunwayPath, end1, centre, 0, runway.WidthMeters, number, designator));
            Paths.Add(new TaxiPath(MsfsCodes.RunwayPath, centre, end2, 0, runway.WidthMeters, number, designator));

            var length = Distance(a, b);
            if (length == 0)
            {
                continue;
            }

            // Unit vector along the runway and the normal pointing toward the apron.
            var (ux, uz) = ((b.X - a.X) / length, (b.Z - a.Z) / length);
            var (nx, nz) = (-uz, ux);
            if (((hub.X - middle.Item1) * nx) + ((hub.Z - middle.Item2) * nz) < 0)
            {
                (nx, nz) = (-nx, -nz);
            }

            var offset = (runway.WidthMeters / 2) + 45;
            var inset = Math.Min(60, length / 4);
            foreach (var (end, point, sign) in new[] { (a, end1, 1.0), (b, end2, -1.0) })
            {
                var hold = Add(frame, (end.X + (ux * inset * sign) + (nx * offset), end.Z + (uz * inset * sign) + (nz * offset)), MsfsCodes.HoldShortPoint);
                Paths.Add(new TaxiPath(MsfsCodes.TaxiwayPath, point, hold, 0, 15));
                Paths.Add(new TaxiPath(MsfsCodes.TaxiwayPath, hold, hubPoint, 0, 15));
            }
        }

        for (var p = 0; p < Parking.Count; p++)
        {
            Paths.Add(new TaxiPath(MsfsCodes.ParkingPath, hubPoint, p, 0, 10));
        }
    }

    private int Add(LocalFrame frame, (double X, double Z) local, int type)
    {
        var (lat, lon) = frame.FromLocal(local.X, local.Z);
        Points.Add(new TaxiPoint(lat, lon, type));
        return Points.Count - 1;
    }
}

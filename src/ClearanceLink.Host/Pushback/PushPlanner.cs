using ClearanceLink.Airports;

namespace ClearanceLink.Host.Pushback;

/// <summary>What the pilot asked for on the PUSH page, or what BeyondATC's clearance said.</summary>
public enum PushChoice
{
    /// <summary>Nose ends up facing the cleared compass direction (BATC's "face east").</summary>
    Face,
    TailLeft,
    TailRight,
    Straight,
}

/// <summary>Where the aircraft is: its reference point and true heading.</summary>
public readonly record struct Pose(double Latitude, double Longitude, double Heading);

/// <summary>A taxiway centreline piece from apt.dat (1202 rows, not runways).</summary>
public readonly record struct TaxiwaySegment(double Lat1, double Lon1, double Lat2, double Lon2, string Name);

/// <summary>
/// A pushback: push straight back <paramref name="StraightMeters"/>, then turn by <paramref name="TurnDegrees"/>
/// (positive: heading grows, so the tail goes left) and stop at <paramref name="FinalHeading"/>.
/// <paramref name="Taxiway"/> names the taxiway it ends on when one was found behind the aircraft.
/// </summary>
public sealed record PushPlan(double StraightMeters, double TurnDegrees, double FinalHeading, string? Taxiway)
{
    public bool TailLeft => TurnDegrees > 0;

    public bool Turns => Math.Abs(TurnDegrees) >= 1;
}

/// <summary>
/// Plans a pushback from the aircraft's pose, the airport's taxiways and the choice. With a taxiway behind the
/// aircraft the push ends on its centreline, lined up with it, facing the way that best matches the choice (BATC
/// says "face east" for a taxiway running 039/219 too). Without one it pushes back a set distance and turns.
/// Pure, so it is tested without X-Plane.
/// </summary>
public static class PushPlanner
{
    /// <summary>Shortest push with no turn, and the push before a turn when there is no taxiway to aim for.</summary>
    public const double DefaultStraightMeters = 15;

    /// <summary>
    /// A push with no turn goes back about one aircraft length, so the aircraft clears its stand. X-Plane has no
    /// reliable length dataref (mapping/simvars.yaml, WING SPAN), so it is taken as 2.5 wheelbases (737-800: 15.6 m
    /// wheelbase, 39.5 m long; A320: 12.6 m, 37.6 m), from the gear positions Better Pushback itself reads.
    /// </summary>
    public const double LengthPerWheelbase = 2.5;

    /// <summary>Longest push with no turn.</summary>
    public const double MaxNoTurnMeters = 60;

    /// <summary>How far a push with no turn goes for an aircraft with this wheelbase (unknown: 15 m).</summary>
    public static double NoTurnMeters(double? wheelbase) => wheelbase is > 0 and var w
        ? Math.Round(Math.Clamp(w * LengthPerWheelbase, DefaultStraightMeters, MaxNoTurnMeters))
        : DefaultStraightMeters;

    /// <summary>Always push at least this far before turning, so the tail clears the stand.</summary>
    public const double MinStraightMeters = 5;

    /// <summary>How far behind the aircraft a taxiway is looked for.</summary>
    public const double SearchMeters = 90;

    /// <summary>
    /// Turn radius of the aircraft's reference point at the steering the driver uses (Citation X at 50%: 0.6 m/s at
    /// 4 deg/s, about 9 m; Ben's PC 2026-10-05). Used only to start the turn early enough to end on the centreline.
    /// </summary>
    public const double TurnRadiusMeters = 9;

    /// <summary>A face direction within this of the current heading is a straight push.</summary>
    public const double StraightWithinDegrees = 30;

    /// <summary>A taxiway crossing the tail line at less than 30 degrees runs along it rather than across.</summary>
    private const double MinCrossingSine = 0.5;

    /// <summary>Largest turn the push makes; a pushback doesn't spin the aircraft round.</summary>
    public const double MaxTurnDegrees = 135;

    public static PushPlan Plan(Pose pose, PushChoice choice, double? faceBearing, IReadOnlyList<TaxiwaySegment> taxiways, double? wheelbase = null)
    {
        var noTurn = new PushPlan(NoTurnMeters(wheelbase), 0, Norm(pose.Heading), null);
        if (choice == PushChoice.Straight || (choice == PushChoice.Face && faceBearing is null))
        {
            return noTurn;
        }

        var behind = choice is PushChoice.Face && Math.Abs(Delta(pose.Heading, faceBearing!.Value)) < StraightWithinDegrees
            ? null
            : TaxiwayBehind(pose, taxiways);
        if (behind is { } t)
        {
            // The taxiway runs both ways; pick the way that matches the choice.
            var ways = new[] { t.Bearing, Norm(t.Bearing + 180) };
            var final = choice switch
            {
                PushChoice.TailLeft => ways.MaxBy(w => Delta(pose.Heading, w)),
                PushChoice.TailRight => ways.MinBy(w => Delta(pose.Heading, w)),
                _ => ways.MinBy(w => Math.Abs(Delta(faceBearing!.Value, w))),
            };
            var turn = Clamp(Delta(pose.Heading, final));
            if (Math.Abs(turn) >= StraightWithinDegrees)
            {
                var straight = Math.Max(MinStraightMeters, t.Distance - (TurnRadiusMeters * Math.Sin(Math.Abs(turn) * Math.PI / 180)));
                return new PushPlan(Math.Round(straight, 1), Math.Round(turn, 1), Norm(pose.Heading + turn), t.Name.Length == 0 ? null : t.Name);
            }
        }

        var fallback = choice switch
        {
            PushChoice.TailLeft => 90,
            PushChoice.TailRight => -90,
            _ => Clamp(Delta(pose.Heading, faceBearing!.Value)),
        };
        return Math.Abs(fallback) < StraightWithinDegrees
            ? noTurn
            : new PushPlan(DefaultStraightMeters, fallback, Norm(pose.Heading + fallback), null);
    }

    /// <summary>The nearest taxiway the line out of the tail crosses within <see cref="SearchMeters"/>.</summary>
    public static (double Distance, double Bearing, string Name)? TaxiwayBehind(Pose pose, IReadOnlyList<TaxiwaySegment> taxiways)
    {
        var frame = new LocalFrame(pose.Latitude, pose.Longitude);
        var rad = pose.Heading * Math.PI / 180;
        var (ux, uz) = (-Math.Sin(rad), -Math.Cos(rad));
        (double Distance, double Bearing, string Name)? best = null;
        foreach (var s in taxiways)
        {
            var (ax, az) = frame.ToLocal(s.Lat1, s.Lon1);
            var (bx, bz) = frame.ToLocal(s.Lat2, s.Lon2);
            var (dx, dz) = (bx - ax, bz - az);
            var cross = (ux * dz) - (uz * dx);
            var length = Math.Sqrt((dx * dx) + (dz * dz));

            // Lines running along the tail (the stand's own lead-in) are not the taxiway to push onto.
            if (length < 1e-6 || Math.Abs(cross) / length < MinCrossingSine)
            {
                continue;
            }

            // Ray P + t*u meets segment A + k*d: t along the tail line, k in [0, 1] along the segment.
            var t = ((ax * dz) - (az * dx)) / cross;
            var k = ((ax * uz) - (az * ux)) / cross;
            if (t < MinStraightMeters || t > SearchMeters || k < 0 || k > 1 || (best is { } b && b.Distance <= t))
            {
                continue;
            }

            best = (t, Norm(Math.Atan2(dx, dz) * 180 / Math.PI), s.Name);
        }

        return best;
    }

    /// <summary>Taxiway pieces of an airport (runways and unusable edges left out).</summary>
    public static IReadOnlyList<TaxiwaySegment> Taxiways(Airport airport)
    {
        var nodes = airport.TaxiNodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
        return airport.TaxiEdges
            .Where(e => !e.IsRunway && e.From != e.To && nodes.ContainsKey(e.From) && nodes.ContainsKey(e.To))
            .Select(e => new TaxiwaySegment(nodes[e.From].Latitude, nodes[e.From].Longitude, nodes[e.To].Latitude, nodes[e.To].Longitude, e.Name.Trim()))
            .ToList();
    }

    /// <summary>Signed heading change from <paramref name="from"/> to <paramref name="to"/>, -180..180 (positive: clockwise).</summary>
    public static double Delta(double from, double to)
    {
        var d = Norm(to - from);
        return d > 180 ? d - 360 : d;
    }

    public static double Norm(double heading) => ((heading % 360) + 360) % 360;

    private static double Clamp(double turn) => Math.Clamp(turn, -MaxTurnDegrees, MaxTurnDegrees);
}

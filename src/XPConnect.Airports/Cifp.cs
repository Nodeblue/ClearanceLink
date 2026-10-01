using System.Globalization;
using XPConnect.Protocol.Facilities;

namespace XPConnect.Airports;

/// <summary>One procedure line of an X-Plane CIFP file (ARINC 424 fields, comma separated).</summary>
public sealed record CifpLeg(
    string Kind,
    string RouteType,
    string Ident,
    string Transition,
    string Fix,
    string FixRegion,
    string FixSection,
    string FixSubsection,
    string Description,
    string PathTerminator,
    double? Theta,
    double? Course,
    char AltitudeDescription,
    double? Altitude1Feet,
    double? Altitude2Feet,
    char SpeedDescription,
    double? SpeedKnots)
{
    /// <summary>Fourth waypoint description character: A/C/D initial approach fix, B/I intermediate, F final approach fix, M missed approach point.</summary>
    public char Role => Description.Length >= 4 ? Description[3] : ' ';
}

/// <summary>A runway threshold from a CIFP RWY line.</summary>
public sealed record CifpRunway(string Name, double Latitude, double Longitude, string Ils);

/// <summary>
/// An airport's procedures from X-Plane's CIFP/&lt;ICAO&gt;.dat, turned into the APPROACH, DEPARTURE and ARRIVAL
/// objects MSFS sends. Field meanings and enums are in docs/protocol/facility-data.md; licensed CIFP files are
/// never committed, so tests use hand-written lines in the same format.
/// </summary>
public sealed class Cifp
{
    public List<CifpLeg> Legs { get; } = [];

    public Dictionary<string, CifpRunway> Runways { get; } = new(StringComparer.Ordinal);

    public static Cifp Parse(TextReader reader)
    {
        var cifp = new Cifp();
        while (reader.ReadLine() is { } line)
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                continue;
            }

            var kind = line[..colon];
            var body = line[(colon + 1)..].TrimEnd().TrimEnd(';');
            try
            {
                if (kind is "SID" or "STAR" or "APPCH")
                {
                    cifp.Legs.Add(ParseLeg(kind, body.Split(',')));
                }
                else if (kind == "RWY")
                {
                    var parts = body.Split(';');
                    var f = parts[0].Split(',');
                    var position = parts.Length > 1 ? parts[1].Split(',') : [];
                    if (position.Length >= 2 && TryArincLatitude(position[0], out var lat) && TryArincLatitude(position[1], out var lon))
                    {
                        var name = f[0].Trim();
                        cifp.Runways[name] = new CifpRunway(name, lat, lon, f.Length > 5 ? f[5].Trim() : "");
                    }
                }
            }
            catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or OverflowException)
            {
                // A malformed line loses that leg, never the airport.
            }
        }

        return cifp;
    }

    private static CifpLeg ParseLeg(string kind, string[] f) => new(
        kind,
        f[1].Trim(),
        f[2].Trim(),
        f[3].Trim(),
        f[4].Trim(),
        f[5].Trim(),
        f[6].Trim(),
        f[7].Trim(),
        f[8],
        f[11].Trim(),
        Tenths(f[18]),
        Tenths(f[20]),
        f[22].Length > 0 ? f[22][0] : ' ',
        Altitude(f[23]),
        Altitude(f[24]),
        f[26].Length > 0 ? f[26][0] : ' ',
        Number(f[27]));

    /// <summary>ILS identifier per runway end ("01L" -> "IBN").</summary>
    public Dictionary<string, string> Ils() =>
        Runways.Values.Where(r => r.Ils.Length > 0).ToDictionary(r => RunwayEnd(r.Name), r => r.Ils, StringComparer.Ordinal);

    /// <summary>Builds APPROACH, DEPARTURE and ARRIVAL objects. Fix positions come from the nav data when given.</summary>
    public (List<FacilityObject> Approaches, List<FacilityObject> Departures, List<FacilityObject> Arrivals) ToFacilities(
        string airport, double latitude, double longitude, NavData? nav)
    {
        var context = new LegContext(this, airport, latitude, longitude, nav);
        var approaches = Legs.Where(l => l.Kind == "APPCH").GroupBy(l => l.Ident).Select(g => Approach(g.Key, [.. g], context)).ToList();
        var departures = Legs.Where(l => l.Kind == "SID").GroupBy(l => l.Ident)
            .Select(g => Procedure("DEPARTURE", g.Key, [.. g], context, runway: ["1", "4", "F", "T"], enroute: ["3", "6", "S", "V"])).ToList();
        var arrivals = Legs.Where(l => l.Kind == "STAR").GroupBy(l => l.Ident)
            .Select(g => Procedure("ARRIVAL", g.Key, [.. g], context, runway: ["3", "6", "9", "S"], enroute: ["1", "4", "7", "F"])).ToList();
        return (approaches, departures, arrivals);
    }

    private sealed record LegContext(Cifp Cifp, string Airport, double Latitude, double Longitude, NavData? Nav);

    private static FacilityObject Approach(string ident, List<CifpLeg> legs, LegContext context)
    {
        var (type, runway, suffix) = ApproachIdent(ident);
        var (number, designator) = MsfsCodes.Runway(runway);
        var approach = new FacilityObject("APPROACH")
            .Set("TYPE", type)
            .Set("SUFFIX", suffix)
            .Set("RUNWAY_NUMBER", number)
            .Set("RUNWAY_DESIGNATOR", designator);

        foreach (var transition in legs.Where(l => l.RouteType == "A").GroupBy(l => l.Transition))
        {
            var obj = new FacilityObject("APPROACH_TRANSITION").Set("TYPE", 1).Set("NAME", transition.Key);
            var first = transition.First();
            obj.Set("IAF_ICAO", first.Fix).Set("IAF_REGION", first.FixRegion);
            AddLegs(obj, "APPROACH_LEG", transition, context);
            approach.Add(obj.Set("N_APPROACH_LEGS", obj.ChildrenOf("APPROACH_LEG").Count));
        }

        // The final route runs to the missed approach point; the legs after it are the missed approach.
        var final = legs.Where(l => l.RouteType != "A" && l.RouteType != "Z").ToList();
        var map = final.FindIndex(l => l.Role == 'M');
        var missed = legs.Where(l => l.RouteType == "Z").ToList();
        if (map >= 0)
        {
            missed.InsertRange(0, final.Skip(map + 1));
            final = final.Take(map + 1).ToList();
        }

        AddLegs(approach, "FINAL_APPROACH_LEG", final, context);
        AddLegs(approach, "MISSED_APPROACH_LEG", missed, context);
        if (final.FirstOrDefault(l => l.Role == 'F') is { } faf)
        {
            approach.Set("FAF_ICAO", faf.Fix).Set("FAF_REGION", faf.FixRegion);
            if (faf.Altitude1Feet is { } fafAltitude)
            {
                approach.Set("FAF_ALTITUDE", fafAltitude * 0.3048);
            }
        }

        return approach
            .Set("N_TRANSITIONS", approach.ChildrenOf("APPROACH_TRANSITION").Count)
            .Set("N_FINAL_APPROACH_LEGS", approach.ChildrenOf("FINAL_APPROACH_LEG").Count)
            .Set("N_MISSED_APPROACH_LEGS", approach.ChildrenOf("MISSED_APPROACH_LEG").Count);
    }

    private static FacilityObject Procedure(string name, string ident, List<CifpLeg> legs, LegContext context, string[] runway, string[] enroute)
    {
        var procedure = new FacilityObject(name).Set("NAME", ident);
        foreach (var group in legs.GroupBy(l => (l.RouteType, l.Transition)))
        {
            // Some common routes are tied to one runway ("RW19R" on an RNAV common route, ISPON2 at YBBN); MSFS presents
            // those as runway transitions.
            var namesRunway = group.Key.Transition.StartsWith("RW", StringComparison.Ordinal) && !enroute.Contains(group.Key.RouteType);
            if (runway.Contains(group.Key.RouteType) || (namesRunway && group.Key.RouteType != "0"))
            {
                foreach (var end in RunwayEnds(group.Key.Transition, context.Cifp))
                {
                    var (number, designator) = MsfsCodes.Runway(end);
                    var transition = new FacilityObject("RUNWAY_TRANSITION").Set("RUNWAY_NUMBER", number).Set("RUNWAY_DESIGNATOR", designator);
                    AddLegs(transition, "APPROACH_LEG", group, context);
                    procedure.Add(transition.Set("N_APPROACH_LEGS", transition.ChildrenOf("APPROACH_LEG").Count));
                }
            }
            else if (enroute.Contains(group.Key.RouteType))
            {
                var transition = new FacilityObject("ENROUTE_TRANSITION").Set("NAME", group.Key.Transition);
                AddLegs(transition, "APPROACH_LEG", group, context);
                procedure.Add(transition.Set("N_APPROACH_LEGS", transition.ChildrenOf("APPROACH_LEG").Count));
            }
            else if (group.Key.RouteType != "0")
            {
                // Common route (engine-out routes, type 0, are left out).
                AddLegs(procedure, "APPROACH_LEG", group, context);
            }
        }

        return procedure
            .Set("N_ENROUTE_TRANSITIONS", procedure.ChildrenOf("ENROUTE_TRANSITION").Count)
            .Set("N_RUNWAY_TRANSITIONS", procedure.ChildrenOf("RUNWAY_TRANSITION").Count)
            .Set("N_APPROACH_LEGS", procedure.ChildrenOf("APPROACH_LEG").Count);
    }

    /// <summary>"RW01L" is one runway end; "RW01B" every parallel 01; "ALL" every runway in the file.</summary>
    private static IEnumerable<string> RunwayEnds(string transition, Cifp cifp)
    {
        if (transition is "ALL" or "")
        {
            return cifp.Runways.Keys.Select(RunwayEnd).Order(StringComparer.Ordinal);
        }

        if (transition.EndsWith('B') && transition.StartsWith("RW", StringComparison.Ordinal))
        {
            var prefix = transition[..^1];
            var matches = cifp.Runways.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && k.Length == transition.Length).ToList();
            if (matches.Count > 0)
            {
                return matches.Select(RunwayEnd).Order(StringComparer.Ordinal);
            }
        }

        return [RunwayEnd(transition)];
    }

    private static string RunwayEnd(string name) => name.StartsWith("RW", StringComparison.Ordinal) ? name[2..] : name;

    private static void AddLegs(FacilityObject parent, string legName, IEnumerable<CifpLeg> legs, LegContext context)
    {
        foreach (var leg in legs)
        {
            parent.Add(Leg(legName, leg, context));
        }
    }

    private static FacilityObject Leg(string name, CifpLeg leg, LegContext context)
    {
        var obj = new FacilityObject(name)
            .Set("TYPE", PathTerminator(leg.PathTerminator))
            .Set("FIX_ICAO", leg.Fix)
            .Set("FIX_REGION", leg.FixRegion)
            .Set("COURSE", leg.Course ?? 0)
            .Set("THETA", leg.Theta ?? 0)
            .Set("SPEED_LIMIT", leg.SpeedKnots ?? -1)
            .Set("APPROACH_SPEED_DESC", leg.SpeedKnots is null ? 0 : leg.SpeedDescription switch { '+' => 2, '@' => 1, _ => 3 })
            .Set("IS_IAF", leg.Role is 'A' or 'C' or 'D' ? 1 : 0)
            .Set("IS_IF", leg.Role is 'B' or 'I' ? 1 : 0)
            .Set("IS_FAF", leg.Role == 'F' ? 1 : 0)
            .Set("IS_MAP", leg.Role == 'M' ? 1 : 0);

        var (description, altitude1, altitude2) = Altitudes(leg);
        obj.Set("APPROACH_ALT_DESC", description).Set("ALTITUDE1", altitude1 * 0.3048).Set("ALTITUDE2", altitude2 * 0.3048);

        if (leg.Fix.Length > 0)
        {
            var kind = FixKind(leg);
            obj.Set("FIX_TYPE", (int)kind);
            if (FixPosition(leg, kind, context) is { } position)
            {
                obj.Set("FIX_LATITUDE", position.Latitude).Set("FIX_LONGITUDE", position.Longitude);
            }
        }

        return obj;
    }

    /// <summary>MSFS APPROACH_ALT_DESC: 0 none, 1 at, 2 at or above, 3 at or below, 4 between (ALTITUDE1 upper, ALTITUDE2 lower).</summary>
    private static (int Description, double Altitude1, double Altitude2) Altitudes(CifpLeg leg)
    {
        var a1 = leg.Altitude1Feet ?? 0;
        var a2 = leg.Altitude2Feet ?? 0;
        if (leg.Altitude1Feet is null && leg.Altitude2Feet is null)
        {
            return (0, 0, 0);
        }

        return leg.AltitudeDescription switch
        {
            '+' or 'H' or 'J' or 'V' => (2, a1, a2),
            '-' => (3, a1, a2),
            'B' => (4, a1, a2),
            'C' => (2, a2, 0),
            _ => (1, a1, a2),
        };
    }

    /// <summary>ARINC path terminators in MSFS's order (alphabetical from AF = 1 to VR = 23).</summary>
    private static readonly string[] PathTerminators =
        ["AF", "CA", "CD", "CF", "CI", "CR", "DF", "FA", "FC", "FD", "FM", "HA", "HF", "HM", "IF", "PI", "RF", "TF", "VA", "VD", "VI", "VM", "VR"];

    public static int PathTerminator(string code) => Array.IndexOf(PathTerminators, code) + 1;

    /// <summary>MSFS FIX_TYPE is the ASCII letter: W waypoint, V VOR, N NDB, R runway, A airport, L localizer.</summary>
    private static char FixKind(CifpLeg leg) => (leg.FixSection, leg.FixSubsection) switch
    {
        ("P", "G") => 'R',
        ("P", "A") => 'A',
        ("P", "I") => 'L',
        ("P", "N") or ("D", "B") => 'N',
        ("D", _) => 'V',
        _ => 'W',
    };

    private static (double Latitude, double Longitude)? FixPosition(CifpLeg leg, char kind, LegContext context)
    {
        if (kind == 'R' && context.Cifp.Runways.TryGetValue(leg.Fix, out var runway))
        {
            return (runway.Latitude, runway.Longitude);
        }

        if (kind == 'A')
        {
            return (context.Latitude, context.Longitude);
        }

        var navKind = kind is 'V' or 'N' or 'L' ? kind : 'W';
        return context.Nav?.Find(leg.Fix, leg.FixRegion, navKind, context.Airport, context.Latitude, context.Longitude) is { } point
            ? (point.Latitude, point.Longitude)
            : null;
    }

    /// <summary>MSFS APPROACH TYPE from the ident's first letter: 1 GPS, 2 VOR, 3 NDB, 4 ILS, 5 LOC, 6 SDF, 7 LDA, 8 VOR/DME, 9 NDB/DME, 10 RNAV, 11 back course.</summary>
    public static (int Type, string Runway, int Suffix) ApproachIdent(string ident)
    {
        var type = ident.Length == 0 ? 0 : ident[0] switch
        {
            'I' or 'G' => 4,
            'L' => 5,
            'B' => 11,
            'D' => 8,
            'V' or 'S' => 2,
            'N' => 3,
            'Q' => 9,
            'R' or 'H' or 'J' => 10,
            'P' => 1,
            'U' => 6,
            'X' => 7,
            _ => 10,
        };

        var rest = ident.Length > 1 ? ident[1..] : "";
        var runway = "";
        if (rest.Length >= 2 && char.IsAsciiDigit(rest[0]) && char.IsAsciiDigit(rest[1]))
        {
            var length = rest.Length >= 3 && rest[2] is 'L' or 'R' or 'C' ? 3 : 2;
            runway = rest[..length];
            rest = rest[length..];
        }
        else if (rest.Length > 0 && !rest.StartsWith('-'))
        {
            // Circling approaches like "VDM-A": the letters before the dash are part of the type, not a runway.
            var dash = rest.IndexOf('-', StringComparison.Ordinal);
            rest = dash >= 0 ? rest[dash..] : "";
        }

        rest = rest.TrimStart('-');
        // MSFS sends '0' (48) when there is no suffix letter (ILS 01L at YBBN).
        var suffix = rest.Length > 0 && char.IsAsciiLetterUpper(rest[^1]) ? rest[^1] : '0';
        return (type, runway, suffix);
    }

    private static double? Tenths(string s) => Number(s) is { } n ? n / 10 : null;

    private static double? Number(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>"06000" is feet, "FL110" a flight level.</summary>
    private static double? Altitude(string s)
    {
        s = s.Trim();
        return s.StartsWith("FL", StringComparison.Ordinal) ? Number(s[2..]) * 100 : Number(s);
    }

    /// <summary>ARINC coordinates: S27225905 is 27 deg 22 min 59.05 s south; E153062449 is 153 deg 06 min 24.49 s east.</summary>
    public static bool TryArincLatitude(string text, out double value)
    {
        value = 0;
        text = text.Trim();
        if (text.Length < 9 || "NSEW".IndexOf(text[0], StringComparison.Ordinal) < 0)
        {
            return false;
        }

        var degreeDigits = text[0] is 'N' or 'S' ? 2 : 3;
        if (text.Length < 1 + degreeDigits + 6 ||
            !int.TryParse(text.AsSpan(1, degreeDigits), NumberStyles.None, CultureInfo.InvariantCulture, out var degrees) ||
            !int.TryParse(text.AsSpan(1 + degreeDigits, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            !int.TryParse(text.AsSpan(3 + degreeDigits), NumberStyles.None, CultureInfo.InvariantCulture, out var hundredths))
        {
            return false;
        }

        value = degrees + (minutes / 60.0) + (hundredths / 100.0 / 3600.0);
        if (text[0] is 'S' or 'W')
        {
            value = -value;
        }

        return true;
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using ClearanceLink.Protocol.Facilities;

namespace ClearanceLink.Airports;

/// <summary>
/// Airports as the X-Plane installation describes them, built into MSFS facility trees on demand: apt.dat in
/// scenery order, procedures from CIFP, fix positions and magnetic variation from the nav data. Indexing runs in
/// the background from <see cref="Start"/>; the first request waits for it. With an index cache file the apt.dat
/// index is kept between starts and only rebuilt when an apt.dat file changes.
/// </summary>
public sealed class XPlaneAirports(XPlaneInstall install, Action<string> log, string? indexCache = null)
{
    private readonly ConcurrentDictionary<string, Lazy<FacilityObject?>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Airport?>> _airports = new(StringComparer.OrdinalIgnoreCase);
    private Task<AptDatIndex>? _index;
    private Task<NavData>? _nav;

    public XPlaneInstall Install => install;

    public void Start()
    {
        _index ??= Task.Run(() =>
        {
            var watch = Stopwatch.StartNew();
            var files = install.AirportFiles();
            var index = AptDatIndex.Build(files, indexCache, log);
            log($"airports: {(index.FromCache ? "loaded the cached index of" : "indexed")} {index.Count} airports in {files.Count} apt.dat files in {watch.ElapsedMilliseconds} ms (first: {files.FirstOrDefault() ?? "none"})");
            return index;
        });
        _nav ??= Task.Run(() =>
        {
            var watch = Stopwatch.StartNew();
            var fixes = install.NavDataFile("earth_fix.dat");
            var navaids = install.NavDataFile("earth_nav.dat");
            var nav = NavData.Load(fixes, navaids);
            log($"airports: loaded {nav.Count} fixes and navaids in {watch.ElapsedMilliseconds} ms from {Path.GetDirectoryName(fixes) ?? "nowhere"}");
            return nav;
        });
    }

    /// <summary>The airport's facility tree, or null when no scenery pack defines it. Built once per airport.</summary>
    public FacilityObject? Find(string icao) => _cache.GetOrAdd(icao.Trim(), key => new Lazy<FacilityObject?>(() => Build(key))).Value;

    /// <summary>The airport as apt.dat describes it (first scenery pack in order), or null. Loaded once per airport.</summary>
    public Airport? Airport(string icao) => _airports.GetOrAdd(icao.Trim(), key => new Lazy<Airport?>(() =>
    {
        Start();
        return _index!.GetAwaiter().GetResult().Load(key);
    })).Value;

    /// <summary>
    /// Custom scenery often lists fewer frequencies than the Gateway airport (Orbx YBBN has no approach or departure),
    /// and BATC hands off by frequency type, so kinds the chosen airport lacks are taken from lower-priority copies.
    /// </summary>
    public static string BorrowFrequencies(Airport airport, IEnumerable<Airport> others)
    {
        var have = airport.Frequencies.Select(f => f.Kind).ToHashSet();
        var added = 0;
        foreach (var other in others)
        {
            var missing = other.Frequencies.Where(f => !have.Contains(f.Kind)).ToList();
            airport.Frequencies.AddRange(missing);
            added += missing.Count;
            have.UnionWith(missing.Select(f => f.Kind));
        }

        return added == 0 ? "" : $" ({added} from lower-priority scenery)";
    }

    private FacilityObject? Build(string icao)
    {
        Start();
        var watch = Stopwatch.StartNew();
        var index = _index!.GetAwaiter().GetResult();
        if (!index.TryGet(icao, out var file, out _))
        {
            return null;
        }

        var airport = index.Load(icao);
        if (airport is null)
        {
            return null;
        }

        var borrowed = BorrowFrequencies(airport, index.LoadOthers(icao));

        var nav = _nav!.GetAwaiter().GetResult();
        var (lat, lon) = airport.ReferencePoint;
        var extras = new AirportExtras { MagneticVariationEast = nav.Variation(lat, lon) };
        var procedures = "no CIFP";
        if (install.CifpFile(icao) is { } cifpPath)
        {
            using var reader = File.OpenText(cifpPath);
            var cifp = Cifp.Parse(reader);
            var (approaches, departures, arrivals) = cifp.ToFacilities(icao, lat, lon, nav);
            extras = new AirportExtras
            {
                MagneticVariationEast = extras.MagneticVariationEast,
                Ils = cifp.Ils(),
                Approaches = approaches,
                Departures = departures,
                Arrivals = arrivals,
            };
            procedures = $"{approaches.Count} approaches, {departures.Count} departures, {arrivals.Count} arrivals";
        }

        var taxi = TaxiNetwork.Build(airport);
        var root = AirportFacility.Build(airport, extras, taxi);
        var generated = taxi.Generated ? " (taxi routes generated)" : "";
        log($"airports: {icao} from {file}: {root["N_RUNWAYS"]} runways, {root["N_TAXI_PARKINGS"]} parking, {root["N_FREQUENCIES"]} frequencies{borrowed}, " +
            $"{root["N_TAXI_POINTS"]} taxi points{generated}, {procedures}, magvar {extras.MagneticVariationEast?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"} in {watch.ElapsedMilliseconds} ms");
        return root;
    }
}

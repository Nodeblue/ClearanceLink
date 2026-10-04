using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace ClearanceLink.Server;

/// <summary>What a client created: an aircraft (BATC's traffic) or another simulated object (pushback tugs).</summary>
public enum TrafficKind
{
    Aircraft,
    SimulatedObject,
}

/// <summary>Ground height under a position, in metres above sea level, or null when not known there.</summary>
public interface IGroundElevation
{
    double? ElevationMeters(double latitude, double longitude);
}

/// <summary>
/// One object a SimConnect client created with an AICreate* call and then flies itself. BATC releases each one from
/// sim AI control and writes its position, attitude and animation state many times a second with
/// SetDataOnSimObject (recording 20260930-174605: 64 objects, about 20 updates per second each).
/// </summary>
public sealed class TrafficObject(uint objectId, int connection, TrafficKind kind, string title, string livery, string tailNumber, DateTimeOffset created)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, double> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, uint> _events = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _eventOrder = new(StringComparer.OrdinalIgnoreCase);
    private long _eventCount;

    public uint ObjectId { get; } = objectId;

    public int Connection { get; } = connection;

    public TrafficKind Kind { get; } = kind;

    /// <summary>The MSFS model title BATC chose, e.g. "FSLTL_B738_Qantas".</summary>
    public string Title { get; } = title;

    public string Livery { get; } = livery;

    public string TailNumber { get; } = tailNumber;

    public DateTimeOffset Created { get; } = created;

    public DateTimeOffset LastUpdate { get; private set; } = created;

    public long Updates { get; private set; }

    public double Latitude => Get("PLANE LATITUDE");

    public double Longitude => Get("PLANE LONGITUDE");

    /// <summary>Feet above sea level, as last written (directly, or as height above ground plus the ground).</summary>
    public double AltitudeFeet => Get("PLANE ALTITUDE");

    public double HeadingTrue => Get("PLANE HEADING DEGREES TRUE");

    /// <summary>MSFS sign convention: positive is nose down.</summary>
    public double PitchDegrees => Get("PLANE PITCH DEGREES");

    /// <summary>MSFS sign convention: positive is left wing down.</summary>
    public double BankDegrees => Get("PLANE BANK DEGREES");

    public double AirspeedKnots => Get("AIRSPEED INDICATED");

    public double GroundSpeedKnots => Get("GROUND VELOCITY");

    public bool OnGround => Get("SIM ON GROUND") != 0;

    /// <summary>Last value written for a SimVar, in the units of <see cref="CanonicalUnits"/>; 0 when never written.</summary>
    public double Get(string simVar)
    {
        lock (_gate)
        {
            return _values.GetValueOrDefault(simVar);
        }
    }

    public bool Has(string simVar)
    {
        lock (_gate)
        {
            return _values.ContainsKey(simVar);
        }
    }

    public IReadOnlyDictionary<string, double> Values
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, double>(_values, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>Last data value of each client event sent to this object (lights, gear, flaps...).</summary>
    public IReadOnlyDictionary<string, uint> Events
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, uint>(_events, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>The units values are kept in, whatever units the client wrote them in.</summary>
    public static string CanonicalUnits(string simVar) => simVar.ToUpperInvariant() switch
    {
        "PLANE LATITUDE" or "PLANE LONGITUDE" => "degrees",
        "PLANE HEADING DEGREES TRUE" or "PLANE HEADING DEGREES MAGNETIC" or "PLANE PITCH DEGREES" or "PLANE BANK DEGREES" => "degrees",
        "PLANE ALTITUDE" or "PLANE ALT ABOVE GROUND" or "PLANE ALT ABOVE GROUND MINUS CG" or "GROUND ALTITUDE" => "feet",
        "AIRSPEED INDICATED" or "AIRSPEED TRUE" or "GROUND VELOCITY" => "knots",
        _ => "",
    };

    internal void Set(string simVar, double value, DateTimeOffset at)
    {
        lock (_gate)
        {
            _values[simVar] = value;
            LastUpdate = at;
            Updates++;
        }
    }

    /// <summary>Which of these events was sent to this object last (GEAR_UP or GEAR_DOWN...); null if none was.</summary>
    public string? LatestEvent(params string[] names)
    {
        lock (_gate)
        {
            return names.Where(_eventOrder.ContainsKey).OrderByDescending(n => _eventOrder[n]).FirstOrDefault();
        }
    }

    internal void SetEvent(string name, uint data)
    {
        lock (_gate)
        {
            _events[name] = data;
            _eventOrder[name] = ++_eventCount;
        }
    }
}

/// <summary>
/// All AI objects clients have created, shared by every connection (BATC creates them on its BeyondATC.exe
/// connection). The status page and the X-Plane traffic plugin read it. Thread-safe.
/// </summary>
public sealed class TrafficRegistry(TimeProvider? time = null)
{
    // Object ids are opaque to the client; MSFS's were large numbers such as 207716368. Start well clear of the user
    // aircraft's 524288 and of zero.
    private int _nextId = 0x0C000000;
    private readonly ConcurrentDictionary<uint, TrafficObject> _objects = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public IReadOnlyList<TrafficObject> Objects => [.. _objects.Values.OrderBy(o => o.ObjectId)];

    public int Count => _objects.Count;

    public TrafficObject? Find(uint objectId) => _objects.GetValueOrDefault(objectId);

    public TrafficObject Create(int connection, TrafficKind kind, string title, string livery, string tailNumber, InitPosition? position)
    {
        var id = (uint)Interlocked.Increment(ref _nextId);
        var now = _time.GetUtcNow();
        var created = new TrafficObject(id, connection, kind, title, livery, tailNumber, now);
        if (position is { } p)
        {
            created.Set("PLANE LATITUDE", p.Latitude, now);
            created.Set("PLANE LONGITUDE", p.Longitude, now);
            created.Set("PLANE ALTITUDE", p.AltitudeFeet, now);
            created.Set("PLANE PITCH DEGREES", p.Pitch, now);
            created.Set("PLANE BANK DEGREES", p.Bank, now);
            created.Set("PLANE HEADING DEGREES TRUE", p.Heading, now);
            created.Set("SIM ON GROUND", p.OnGround ? 1 : 0, now);
            created.Set("AIRSPEED INDICATED", p.AirspeedKnots, now);
        }

        _objects[id] = created;
        return created;
    }

    public bool Remove(uint objectId) => _objects.TryRemove(objectId, out _);

    /// <summary>Drops everything a connection created (it disconnected; MSFS removes them too).</summary>
    public int RemoveConnection(int connection)
    {
        var gone = _objects.Values.Where(o => o.Connection == connection).Select(o => o.ObjectId).ToList();
        gone.ForEach(id => _objects.TryRemove(id, out _));
        return gone.Count;
    }

    internal DateTimeOffset Now => _time.GetUtcNow();
}

/// <summary>SIMCONNECT_DATA_INITPOSITION: where an AI object starts. 56 bytes on the wire.</summary>
public readonly record struct InitPosition(double Latitude, double Longitude, double AltitudeFeet, double Pitch, double Bank, double Heading, bool OnGround, double AirspeedKnots)
{
    public const int Size = 56;

    /// <summary>Reads the struct; airspeed is a DWORD of knots (or -1 cruise, -2 keep), kept as given.</summary>
    public static InitPosition? Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
        {
            return null;
        }

        return new InitPosition(
            BinaryPrimitives.ReadDoubleLittleEndian(data),
            BinaryPrimitives.ReadDoubleLittleEndian(data[8..]),
            BinaryPrimitives.ReadDoubleLittleEndian(data[16..]),
            BinaryPrimitives.ReadDoubleLittleEndian(data[24..]),
            BinaryPrimitives.ReadDoubleLittleEndian(data[32..]),
            BinaryPrimitives.ReadDoubleLittleEndian(data[40..]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[48..]) != 0,
            BinaryPrimitives.ReadInt32LittleEndian(data[52..]));
    }
}

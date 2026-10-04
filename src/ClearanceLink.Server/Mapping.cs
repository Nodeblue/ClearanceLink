using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ClearanceLink.Server;

/// <summary>How one MSFS SimVar is produced from X-Plane. Loaded from <c>mapping/simvars.yaml</c>.</summary>
public sealed class SimVarMap
{
    public string Name { get; set; } = "";

    /// <summary>True when the SimVar takes an index suffix (<c>COM ACTIVE FREQUENCY:1</c>); <c>{i}</c> in datarefs is replaced by it.</summary>
    public bool Indexed { get; set; }

    public string? Dataref { get; set; }

    /// <summary>Units of the source value. Requests in other units are converted.</summary>
    public string Units { get; set; } = "number";

    /// <summary>Fixed value when X-Plane has no equivalent.</summary>
    public double? Const { get; set; }

    public string? ConstText { get; set; }

    /// <summary>Source is a byte-array dataref holding text.</summary>
    public bool Text { get; set; }

    /// <summary>Named derivation, see <see cref="Derivations"/>.</summary>
    public string? Derive { get; set; }

    public List<string> Inputs { get; set; } = [];

    /// <summary>Lookup table from the X-Plane value to the MSFS value (used with <c>derive: map</c>).</summary>
    public Dictionary<int, double> Map { get; set; } = [];

    /// <summary>implemented | emulated | stubbed | unsupported.</summary>
    public string Status { get; set; } = "implemented";

    /// <summary>True until the dataref name, units and sign have been checked against a running X-Plane 12.</summary>
    public bool Verify { get; set; }

    public string? Notes { get; set; }
}

/// <summary>How one MSFS client event acts on X-Plane. Loaded from <c>mapping/events.yaml</c>.</summary>
public sealed class EventMap
{
    public string Name { get; set; } = "";

    /// <summary>Dataref the event's data value is written to.</summary>
    public string? Dataref { get; set; }

    /// <summary>Units of the event data (for example <c>hz</c> or <c>bco16</c>).</summary>
    public string FromUnits { get; set; } = "number";

    /// <summary>Units the dataref expects.</summary>
    public string ToUnits { get; set; } = "number";

    /// <summary>X-Plane command to run instead of a dataref write.</summary>
    public string? Command { get; set; }

    public string Status { get; set; } = "implemented";

    public string? Notes { get; set; }
}

public sealed class MappingSet
{
    private readonly Dictionary<string, SimVarMap> _simVars;
    private readonly Dictionary<string, EventMap> _events;

    public MappingSet(IEnumerable<SimVarMap> simVars, IEnumerable<EventMap> events)
    {
        _simVars = simVars.ToDictionary(m => Key(m.Name), StringComparer.Ordinal);
        _events = events.ToDictionary(m => Key(m.Name), StringComparer.Ordinal);
    }

    public IReadOnlyCollection<SimVarMap> SimVars => _simVars.Values;

    public IReadOnlyCollection<EventMap> Events => _events.Values;

    public static MappingSet Empty { get; } = new([], []);

    /// <summary>Loads <c>simvars.yaml</c> and <c>events.yaml</c> from a directory.</summary>
    public static MappingSet LoadDirectory(string directory) =>
        new(
            LoadList<SimVarMap>(Path.Combine(directory, "simvars.yaml")),
            LoadList<EventMap>(Path.Combine(directory, "events.yaml")));

    public static List<T> ParseList<T>(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
        return deserializer.Deserialize<List<T>>(yaml) ?? [];
    }

    /// <summary>Finds the mapping for a requested SimVar such as <c>COM ACTIVE FREQUENCY:1</c>; returns the index too.</summary>
    public SimVarMap? FindSimVar(string requested, out int index)
    {
        index = 0;
        var name = requested.Trim();
        var colon = name.LastIndexOf(':');
        if (colon > 0 && int.TryParse(name[(colon + 1)..], out var i))
        {
            if (_simVars.TryGetValue(Key(name[..colon]), out var indexed) && indexed.Indexed)
            {
                index = i;
                return indexed;
            }
        }

        return _simVars.GetValueOrDefault(Key(name));
    }

    public EventMap? FindEvent(string name) => _events.GetValueOrDefault(Key(name));

    private static List<T> LoadList<T>(string path) => File.Exists(path) ? ParseList<T>(File.ReadAllText(path)) : [];

    private static string Key(string name) => name.Trim().ToUpperInvariant();
}

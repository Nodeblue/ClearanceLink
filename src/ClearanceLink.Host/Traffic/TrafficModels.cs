using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ClearanceLink.Host.Traffic;

/// <summary>
/// What the plugin is asked to draw for one BATC model title: an ICAO type and, when known, an ICAO airline and the
/// CSL livery key of an imported FSLTL livery (an exact match in XPMP2).
/// </summary>
public readonly record struct TrafficModel(string IcaoType, string? IcaoAirline, string? Livery = null);

/// <summary>
/// Turns BATC's MSFS model titles (FSLTL_B738_Qantas, Asobo PassiveAircraft King Air 350i) into an ICAO type and
/// airline for the plugin's CSL matching. The rules are data: mapping/traffic-models.yaml.
/// </summary>
public sealed partial class TrafficModels
{
    private const string AsoboPrefix = "Asobo PassiveAircraft ";

    private readonly HashSet<string> _prefixes;
    private readonly Dictionary<string, string> _types;
    private readonly HashSet<string> _notAirlines;
    private readonly List<(string[] Words, string Code)> _airlines;
    private readonly List<(string Start, string Type)> _asobo;
    private readonly string _defaultType;
    private readonly Dictionary<string, double> _groundPitch;
    private readonly Dictionary<string, double> _groundOffset;

    private TrafficModels(File file)
    {
        _prefixes = new(file.Prefixes ?? [], StringComparer.OrdinalIgnoreCase);
        _types = new(file.Types ?? [], StringComparer.OrdinalIgnoreCase);
        _notAirlines = new(file.NotAirlines ?? [], StringComparer.OrdinalIgnoreCase);
        _airlines = [.. (file.Airlines ?? []).Select(a => (Words(a.Key), a.Value)).OrderByDescending(a => a.Item1.Length)];
        _asobo = [.. (file.Asobo ?? []).Select(a => (a.Key, a.Value)).OrderByDescending(a => a.Key.Length)];
        _defaultType = string.IsNullOrWhiteSpace(file.DefaultType) ? "A320" : file.DefaultType;
        _groundPitch = new(file.GroundPitch ?? [], StringComparer.OrdinalIgnoreCase);
        _groundOffset = new(file.GroundOffsetFt ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public static TrafficModels Parse(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        return new TrafficModels(deserializer.Deserialize<File>(yaml) ?? new File());
    }

    public static TrafficModels Load(string path) => Parse(System.IO.File.ReadAllText(path));

    public static TrafficModels Empty { get; } = Parse("{}");

    /// <summary>Pitch (degrees, nose up positive) to draw a parked or slow-taxiing aircraft of this type with.</summary>
    public double GroundPitch(string icaoType) => _groundPitch.GetValueOrDefault(icaoType, _groundPitch.GetValueOrDefault("default"));

    /// <summary>Feet to raise (+) or lower (-) a model of this type when it is drawn on the ground.</summary>
    public double GroundOffsetFeet(string icaoType) => _groundOffset.GetValueOrDefault(icaoType, _groundOffset.GetValueOrDefault("default"));

    private Dictionary<string, TrafficModel> _imported = new(StringComparer.Ordinal);

    /// <summary>Titles of imported FSLTL liveries (clearancelink fsltl import) match their own CSL livery exactly.</summary>
    public void UseImported(IEnumerable<(string Title, string Key, string IcaoType, string IcaoAirline)> liveries)
    {
        var imported = new Dictionary<string, TrafficModel>(StringComparer.Ordinal);
        foreach (var l in liveries)
        {
            if (l.IcaoType.Length == 0) continue;
            imported[l.Title] = new TrafficModel(l.IcaoType, l.IcaoAirline.Length > 0 ? l.IcaoAirline : null, l.Key);
        }

        _imported = imported;
    }

    public int ImportedCount => _imported.Count;

    public TrafficModel Match(string title)
    {
        if (_imported.TryGetValue(title, out var exact)) return exact;
        if (title.StartsWith(AsoboPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var rest = title[AsoboPrefix.Length..];
            var type = _asobo.FirstOrDefault(a => rest.StartsWith(a.Start, StringComparison.OrdinalIgnoreCase)).Type;
            return new TrafficModel(type ?? _defaultType, null);
        }

        var parts = title.Split('_');
        if (parts.Length < 2 || !parts[0].Equals("FSLTL", StringComparison.OrdinalIgnoreCase))
        {
            return new TrafficModel(_defaultType, Airline(Words(title), title));
        }

        var i = 1;
        while (i < parts.Length - 1 && _prefixes.Contains(parts[i]))
        {
            i++;
        }

        var word = parts[i].ToUpperInvariant();
        if (_types.TryGetValue(word, out var mapped))
        {
            word = mapped;
        }
        else if (word.Length == 5 && word.EndsWith('F'))
        {
            word = word[..4];
            word = _types.GetValueOrDefault(word, word);
        }

        var airlinePart = string.Join('_', parts.Skip(i + 1));
        return new TrafficModel(IcaoTypeWord().IsMatch(word) ? word : _defaultType, Airline(Words(airlinePart), airlinePart));
    }

    private string? Airline(string[] words, string text)
    {
        // FSLTL's own convention: "QFA-Qantas", "SIA-Singapore Airlines".
        if (CodeBeforeDash().Match(text) is { Success: true } coded && !_notAirlines.Contains(coded.Groups[1].Value))
        {
            return coded.Groups[1].Value;
        }

        foreach (var (name, code) in _airlines)
        {
            for (var start = 0; start + name.Length <= words.Length; start++)
            {
                if (name.Select((w, k) => w.Equals(words[start + k], StringComparison.OrdinalIgnoreCase)).All(x => x))
                {
                    return code;
                }
            }
        }

        return words.FirstOrDefault(w => w.Length == 3 && w.All(char.IsAsciiLetterUpper) && !_notAirlines.Contains(w));
    }

    private static string[] Words(string text) => text.Split(['_', '-', ' '], StringSplitOptions.RemoveEmptyEntries);

    [GeneratedRegex("^([A-Z]{3})-")]
    private static partial Regex CodeBeforeDash();

    [GeneratedRegex("^[A-Z][A-Z0-9]{1,3}$")]
    private static partial Regex IcaoTypeWord();

    private sealed class File
    {
        public List<string>? Prefixes { get; set; }

        public Dictionary<string, string>? Types { get; set; }

        public List<string>? NotAirlines { get; set; }

        public Dictionary<string, string>? Airlines { get; set; }

        public Dictionary<string, string>? Asobo { get; set; }

        public string? DefaultType { get; set; }

        public Dictionary<string, double>? GroundPitch { get; set; }

        public Dictionary<string, double>? GroundOffsetFt { get; set; }
    }
}

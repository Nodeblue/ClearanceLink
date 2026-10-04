namespace XPConnect.Fsltl;

/// <summary>
/// MSFS .cfg files (aircraft.cfg, model.CFG, texture.CFG): INI sections, <c>key = value ; comment</c>, values optionally
/// quoted. Section and key names are case-insensitive.
/// </summary>
public sealed class CfgFile
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);

    public static CfgFile Parse(string text)
    {
        var cfg = new CfgFile();
        Dictionary<string, string>? section = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '/') continue;
            if (line[0] == '[')
            {
                var end = line.IndexOf(']', StringComparison.Ordinal);
                var name = end > 0 ? line[1..end].Trim() : line[1..].Trim();
                if (!cfg._sections.TryGetValue(name, out section))
                {
                    section = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    cfg._sections[name] = section;
                }
                continue;
            }

            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (section is null || eq <= 0) continue;
            section[line[..eq].Trim()] = Value(line[(eq + 1)..]);
        }

        return cfg;
    }

    public static CfgFile Load(string path) => Parse(File.ReadAllText(path));

    public IEnumerable<string> Sections => _sections.Keys;

    public string? Get(string section, string key) =>
        _sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value) ? value : null;

    /// <summary>The value up to an unquoted ';', without surrounding quotes.</summary>
    private static string Value(string text)
    {
        text = text.Trim();
        if (text.StartsWith('"'))
        {
            var close = text.IndexOf('"', 1);
            return close > 0 ? text[1..close] : text[1..];
        }

        var semicolon = text.IndexOf(';', StringComparison.Ordinal);
        return (semicolon >= 0 ? text[..semicolon] : text).Trim();
    }
}

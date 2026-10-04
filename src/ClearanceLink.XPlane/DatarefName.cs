using System.Globalization;

namespace ClearanceLink.XPlane;

/// <summary>A dataref name as used in mappings: <c>sim/foo/bar</c> or an array element <c>sim/foo/bar[2]</c>.</summary>
public readonly record struct DatarefName(string Name, int? Index)
{
    public static DatarefName Parse(string text)
    {
        var s = text.Trim();
        if (s.EndsWith(']') && s.LastIndexOf('[') is var open and > 0 &&
            int.TryParse(s[(open + 1)..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            return new DatarefName(s[..open], index);
        }

        return new DatarefName(s, null);
    }

    public override string ToString() => Index is { } i ? $"{Name}[{i}]" : Name;
}

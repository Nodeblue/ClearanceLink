using System.Text;
using System.Text.RegularExpressions;

namespace ClearanceLink.Host.Pushback;

/// <summary>A pushback approval BeyondATC gave the pilot: when, and the way the nose should end up pointing, if it said.</summary>
public sealed record PushClearance(DateTimeOffset At, double? FaceBearing, string? FaceWord, string Text);

/// <summary>
/// Reads BeyondATC's pushback approvals from its Player.log. BATC writes each instruction to the player on one line,
/// e.g. "[Instruction] N505SB, Brisbane Ground, pushback and engine start approved. Face east." (BATC 1.8.21,
/// Ben's PC 2026-10-05; docs/research/batc-gsx-pushback.md). Other aircraft's calls are [LocalVoiceInput] lines,
/// so only [Instruction] lines count.
/// </summary>
public static partial class BatcClearance
{
    /// <summary>The log's usual place: %USERPROFILE%\AppData\LocalLow\Skirmish Mode Games, Inc\BeyondATC\Player.log.</summary>
    public static string DefaultLog => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Skirmish Mode Games, Inc", "BeyondATC", "Player.log");

    private static readonly (string Word, double Bearing)[] Compass =
    [
        ("northeast", 45), ("north east", 45), ("northwest", 315), ("north west", 315),
        ("southeast", 135), ("south east", 135), ("southwest", 225), ("south west", 225),
        ("north", 0), ("east", 90), ("south", 180), ("west", 270),
    ];

    [GeneratedRegex(@"^\[Instruction\]\s*(?<text>.*\bpush\s?back\b.*\bapproved\b.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Approved();

    [GeneratedRegex(@"\bfac(?:e|ing)\s+(?<dir>(?:north|south)\s?(?:east|west)|north|south|east|west)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Facing();

    /// <summary>The approval on this log line, or null when the line is anything else.</summary>
    public static PushClearance? Parse(string line, DateTimeOffset at)
    {
        var approved = Approved().Match(line.Trim());
        if (!approved.Success)
        {
            return null;
        }

        var text = approved.Groups["text"].Value.Trim();
        var facing = Facing().Match(text);
        if (!facing.Success)
        {
            return new PushClearance(at, null, null, text);
        }

        var word = Regex.Replace(facing.Groups["dir"].Value.ToLowerInvariant(), @"\s+", " ");
        var bearing = Compass.First(c => c.Word == word).Bearing;
        return new PushClearance(at, bearing, word.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant(), text);
    }
}

/// <summary>
/// Follows Player.log as BeyondATC writes it: reads only what was added since the last look, starts over when BATC
/// starts a new log (the file gets shorter), and never locks the file (BATC keeps it open for writing).
/// </summary>
public sealed class BatcLogFollower(string path, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private long _offset = -1;
    private string _partial = "";

    public string Path => path;

    /// <summary>New approvals since the last call. The first call only finds the end of the file: old approvals are history.</summary>
    public IReadOnlyList<PushClearance> Poll()
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists)
            {
                return [];
            }
        }
        catch (IOException)
        {
            return [];
        }

        if (_offset < 0 || info.Length < _offset)
        {
            // First look: skip what is already there. A shorter file is a new BATC session: read it from the start.
            _offset = _offset < 0 ? info.Length : 0;
            _partial = "";
            if (_offset > 0)
            {
                return [];
            }
        }

        if (info.Length == _offset)
        {
            return [];
        }

        string added;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(_offset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            added = reader.ReadToEnd();
            _offset = stream.Position;
        }
        catch (IOException)
        {
            return [];
        }

        var text = _partial + added;
        var end = text.LastIndexOf('\n');
        _partial = end < 0 ? text : text[(end + 1)..];
        if (end < 0)
        {
            return [];
        }

        var now = _time.GetUtcNow();
        return text[..end].Split('\n').Select(l => BatcClearance.Parse(l.TrimEnd('\r'), now)).OfType<PushClearance>().ToList();
    }
}

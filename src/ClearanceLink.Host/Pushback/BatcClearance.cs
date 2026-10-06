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

    [GeneratedRegex(@"^\[Instruction\]\s*(?!.*\bnot\s+(?:yet\s+)?approved\b)(?<text>.*\bpush\s?back\b.*\bapproved\b.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
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
    private DateTime _created;
    private byte[] _partial = [];

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

        if (_offset < 0)
        {
            // First look: skip what is already there.
            (_offset, _created) = (info.Length, info.CreationTimeUtc);
            return [];
        }

        // Windows keeps a file's creation time; elsewhere it is the last change, so only the length tells there.
        if (info.Length < _offset || (OperatingSystem.IsWindows() && info.CreationTimeUtc != _created))
        {
            // A shorter or newly made file is a new BATC session: read it from the start.
            (_offset, _created, _partial) = (0, info.CreationTimeUtc, []);
        }

        if (info.Length == _offset)
        {
            return [];
        }

        byte[] added;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(_offset, SeekOrigin.Begin);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            added = buffer.ToArray();
            _offset = stream.Position;
        }
        catch (IOException)
        {
            return [];
        }

        // Only whole lines are decoded, so a line (or a UTF-8 character) cut at the end of the file waits for the rest.
        var bytes = _partial.Length == 0 ? added : [.. _partial, .. added];
        var end = Array.LastIndexOf(bytes, (byte)'\n');
        _partial = bytes[(end + 1)..];
        if (end < 0)
        {
            return [];
        }

        var now = _time.GetUtcNow();
        return Encoding.UTF8.GetString(bytes, 0, end).Split('\n').Select(l => BatcClearance.Parse(l.TrimEnd('\r'), now)).OfType<PushClearance>().ToList();
    }
}

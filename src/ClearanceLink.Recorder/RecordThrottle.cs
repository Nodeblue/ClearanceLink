using System.Buffers.Binary;
using System.Collections.Concurrent;
using ClearanceLink.Protocol;

namespace ClearanceLink.Recorder;

/// <summary>
/// Samples the high-rate streams so a recording stays a manageable size. BATC writes AI aircraft positions
/// (SetDataOnSimObject) and polls user-aircraft data (SimObjectData) hundreds of times a second; one sample per
/// stream per interval is plenty for analysis. Everything else is always recorded. Forwarding is never affected.
/// </summary>
public sealed class RecordThrottle(TimeSpan interval, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<(int Conn, uint Type, uint A, uint B), long> _lastRecorded = new();
    private readonly ConcurrentDictionary<string, long> _skipped = new(StringComparer.Ordinal);

    public static RecordThrottle None { get; } = new(TimeSpan.Zero);

    public TimeSpan Interval { get; } = interval;

    /// <summary>Packets left out of the recording, by call or message name.</summary>
    public IReadOnlyDictionary<string, long> Skipped => _skipped;

    public bool ShouldRecord(Packet packet, int connection)
    {
        if (Interval <= TimeSpan.Zero || StreamKey(packet, connection) is not { } key)
        {
            return true;
        }

        var now = _time.GetTimestamp();
        var last = _lastRecorded.GetOrAdd(key, long.MinValue);
        if (last != long.MinValue && _time.GetElapsedTime(last, now) < Interval)
        {
            _skipped.AddOrUpdate(packet.Name, 1, (_, n) => n + 1);
            return false;
        }

        _lastRecorded[key] = now;
        return true;
    }

    private static (int, uint, uint, uint)? StreamKey(Packet packet, int connection)
    {
        var payload = packet.Payload;
        if (payload.Length < 8)
        {
            return null;
        }

        var first = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        var second = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);
        return packet.Direction switch
        {
            // SetDataOnSimObject: defineId, objectId
            Direction.ClientToServer when packet.FunctionId == 0x10 => (connection, packet.TypeField, first, second),
            // SimObjectData(ByType): requestId, objectId
            Direction.ServerToClient when (RecvId)packet.TypeField is RecvId.SimObjectData or RecvId.SimObjectDataByType
                => (connection, packet.TypeField, first, second),
            _ => null,
        };
    }
}

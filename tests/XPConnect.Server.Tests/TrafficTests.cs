using System.Buffers.Binary;
using XPConnect.Protocol;
using XPConnect.Recorder;

namespace XPConnect.Server.Tests;

/// <summary>
/// Replays the start of BATC's traffic on its BeyondATC.exe connection (recordings/samples/batc-1.8.21-traffic.jsonl:
/// 46 creations at YBBN in the first 40 seconds, then position writes and ground-height polls for two aircraft) and checks the answers
/// against what MSFS 2024 sent.
/// </summary>
public sealed class TrafficTests
{
    private const double YbbnElevationMeters = 3.66;

    private static string TrafficSample => Path.Combine(Fixtures.RepoRoot, "recordings", "samples", "batc-1.8.21-traffic.jsonl");

    private readonly List<Packet> _sent = [];

    [Fact]
    public async Task Batc_traffic_is_created_moved_and_kept_on_the_ground()
    {
        var entries = JsonlRecorder.ReadAll(TrafficSample).ToList();
        var clock = new RecordedClock(entries[0].Time);
        var traffic = new TrafficRegistry(clock);
        var session = new SimConnectSession(
            2,
            ServerIdentity.Msfs2024,
            new SimVarResolver(Fixtures.Mappings(), Fixtures.ParkedAtYbbn()),
            Fixtures.ParkedAtYbbn(),
            p =>
            {
                _sent.Add(p);
                return Task.CompletedTask;
            },
            _ => { },
            time: clock,
            traffic: traffic,
            ground: new FixedGround(YbbnElevationMeters),
            liveries: [("FSLTL_B738_Qantas", ""), ("Asobo PassiveAircraft A320neo", "A320NEO_0")]);

        // MSFS's object ids in the recording, by the creating request id, so later packets can be readdressed.
        var msfsIds = entries.Where(e => e.Name == "AssignedObjectId")
            .ToDictionary(e => uint.Parse(e.Field("objectId")!, System.Globalization.CultureInfo.InvariantCulture), e => uint.Parse(e.Field("requestId")!, System.Globalization.CultureInfo.InvariantCulture));
        var ourIds = new Dictionary<uint, uint>();
        var msfsAnswers = new List<RecordEntry>();
        var creations = 0;
        foreach (var entry in entries)
        {
            if (entry.Dir == "s2c")
            {
                if (entry.Name == "SimObjectData")
                {
                    msfsAnswers.Add(entry);
                }

                continue;
            }

            clock.Now = entry.Time;
            var packet = Readdress(entry.ToPacket(), msfsIds, ourIds);
            var before = _sent.Count;
            await session.HandleAsync(packet);
            if (packet.Name.StartsWith("AICreate", StringComparison.Ordinal))
            {
                creations++;
                var assigned = PacketDecoder.Decode(_sent[before]);
                Assert.Equal(RecvId.AssignedObjectId, (RecvId)_sent[before].TypeField);
                Assert.Equal(PacketDecoder.Decode(packet)["requestId"], assigned["requestId"]);
                ourIds[(uint)PacketDecoder.Decode(packet)["requestId"]!] = (uint)assigned["objectId"]!;
            }
        }

        // The model list went out in one packet (2 entries), entry 0 of 1.
        var list = _sent.Single(p => (RecvId)p.TypeField == RecvId.EnumerateSimObjectAndLiveryList);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(list.Payload[4..]));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(list.Payload[12..]));

        Assert.Equal(46, creations);
        Assert.Equal(46, traffic.Count);
        Assert.Equal(4, traffic.Objects.Count(o => o.Kind == TrafficKind.SimulatedObject)); // pushback tugs
        var qantas = traffic.Objects.First(o => o.Title == "FSLTL_B738_Qantas");
        Assert.Equal("VHXZE", qantas.TailNumber);
        Assert.InRange(qantas.Latitude, -27.45, -27.35);
        Assert.InRange(qantas.Longitude, 153.05, 153.2);

        // Every ONCE poll for an AI aircraft got an answer, and its ground altitude is YBBN's elevation, within the
        // same few feet as MSFS's own terrain there.
        var ours = _sent.Where(p => (RecvId)p.TypeField == RecvId.SimObjectData).ToList();
        Assert.Equal(msfsAnswers.Count, ours.Count);
        for (var i = 0; i < ours.Count; i++)
        {
            var defineId = BinaryPrimitives.ReadUInt32LittleEndian(ours[i].Payload[8..]);
            var data = ours[i].Payload[28..];
            var msfs = Convert.FromHexString(msfsAnswers[i].Field("dataHex")!);
            Assert.Equal(msfs.Length, data.Length);
            if (defineId == 18)
            {
                // GROUND ALTITUDE, PLANE ALTITUDE (feet), GROUND VELOCITY (knots). Both polled aircraft are arriving,
                // 5,000 to 22,000 ft up, where MSFS's terrain was 35 to 330 ft and ours falls back to the ground at
                // YBBN; altitude and speed come from BATC's own writes and match MSFS's answers closely.
                Assert.InRange(BinaryPrimitives.ReadDoubleLittleEndian(data), 0, 400);
                Assert.InRange(BinaryPrimitives.ReadDoubleLittleEndian(data[8..]), BinaryPrimitives.ReadDoubleLittleEndian(msfs.AsSpan(8)) - 100, BinaryPrimitives.ReadDoubleLittleEndian(msfs.AsSpan(8)) + 100);
                if (i >= 4)
                {
                    Assert.InRange(BinaryPrimitives.ReadDoubleLittleEndian(data[16..]), BinaryPrimitives.ReadDoubleLittleEndian(msfs.AsSpan(16)) - 25, BinaryPrimitives.ReadDoubleLittleEndian(msfs.AsSpan(16)) + 25);
                }
            }
        }
    }

    [Fact]
    public void Init_position_reads_the_recorded_struct()
    {
        // VHXZE's AICreateNonATCAircraft init position: a B738 parked at YBBN, heading 263 degrees.
        var p = InitPosition.Read(Convert.FromHexString("C391F9FB3F623BC0D5378DD3E223634000000040E17A34400000000000000000000000000000000000000020537270400100000000000000"))!.Value;

        Assert.Equal(-27.38, p.Latitude, 2);
        Assert.Equal(153.12, p.Longitude, 2);
        Assert.Equal(20.48, p.AltitudeFeet, 2);
        Assert.Equal(263.1, p.Heading, 1);
        Assert.True(p.OnGround);
    }

    /// <summary>Rewrites MSFS object ids in client packets to the ids our session handed out for the same request.</summary>
    private static Packet Readdress(Packet packet, Dictionary<uint, uint> msfsIdToRequest, Dictionary<uint, uint> ourIdByRequest)
    {
        var offset = packet.FunctionId switch
        {
            0x05 or 0x2B or 0x2C => 0, // TransmitClientEvent, AIReleaseControl, AIRemoveObject: objectId first
            0x10 => 4, // SetDataOnSimObject: defineId, objectId
            0x0E => 8, // RequestDataOnSimObject: requestId, defineId, objectId
            _ => -1,
        };
        if (offset < 0)
        {
            return packet;
        }

        var raw = (byte[])packet.Raw.Clone();
        var at = Packet.ClientHeaderSize + offset;
        var msfsId = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(at));
        if (msfsIdToRequest.TryGetValue(msfsId, out var request) && ourIdByRequest.TryGetValue(request, out var ours))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(at), ours);
        }

        return new Packet(Direction.ClientToServer, raw);
    }

    /// <summary>Plays the recording's timestamps, so ground speed from successive positions comes out as it did live.</summary>
    private sealed class RecordedClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FixedGround(double meters) : IGroundElevation
    {
        public double? ElevationMeters(double latitude, double longitude) => meters;
    }
}

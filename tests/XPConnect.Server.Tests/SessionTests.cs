using System.Buffers.Binary;
using XPConnect.Protocol;
using XPConnect.Protocol.Facilities;
using XPConnect.Protocol.Tests;
using XPConnect.Server;

namespace XPConnect.Server.Tests;

/// <summary>Drives a session with BATC's recorded setup packets and checks what it would send back.</summary>
public sealed class SessionTests
{
    private readonly List<Packet> _sent = [];
    private readonly List<string> _log = [];
    private readonly InMemoryXPlane _xp = Fixtures.ParkedAtYbbn();

    [Fact]
    public async Task Batc_setup_gets_open_reply_and_define_17_from_xplane()
    {
        var session = NewSession();
        foreach (var packet in Fixtures.ClientPackets(2))
        {
            await session.HandleAsync(packet);
        }

        Assert.Equal(RecvId.Open, (RecvId)_sent[0].TypeField);
        Assert.Equal("SunRise", PacketDecoder.Decode(_sent[0])["applicationName"]);
        Assert.Equal("BATCDLLSimConnection", session.ClientName);

        // The fixture ends with three ONCE requests for define 17: one reply each.
        var data = _sent.Where(p => (RecvId)p.TypeField == RecvId.SimObjectData).ToList();
        Assert.Equal(3, data.Count);
        var header = data[0].Payload;
        Assert.Equal(1004u, U32(header, 0));
        Assert.Equal(SimConnectSession.UserObjectId, U32(header, 4));
        Assert.Equal(17u, U32(header, 8));
        Assert.Equal(35u, U32(header, 24)); // define count

        // Define 17 starts with PLANE LATITUDE, PLANE LONGITUDE (FLOAT64 degrees), then INDICATED ALTITUDE CALIBRATED.
        var block = header[28..];
        Assert.Equal(-27.4016, BinaryPrimitives.ReadDoubleLittleEndian(block), 6);
        Assert.Equal(153.1122, BinaryPrimitives.ReadDoubleLittleEndian(block[8..]), 6);
        // Calibrated altitude is the altimeter reading at the local QNH: about 15 ft at YBBN, as MSFS sent.
        Assert.InRange(BinaryPrimitives.ReadDoubleLittleEndian(block[16..]), 10, 20);
        Assert.Equal(ExpectedDefine17Size, block.Length);
        Assert.Empty(session.Stats.UnmappedNames.Where(n => !n.StartsWith("L:", StringComparison.Ordinal)).Intersect(Define17Names));
    }

    [Fact]
    public async Task Helper_define_300_is_sent_every_second()
    {
        var clock = new ManualClock();
        var session = NewSession(clock);
        foreach (var packet in Fixtures.ClientPackets(1))
        {
            await session.HandleAsync(packet);
        }

        await session.TickAsync();
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await session.TickAsync();
        clock.Advance(TimeSpan.FromMilliseconds(600));
        await session.TickAsync();

        var data = _sent.Where(p => (RecvId)p.TypeField == RecvId.SimObjectData).ToList();
        Assert.Equal(2, data.Count);
        Assert.Equal(300u, U32(data[0].Payload, 8));

        // ATC TYPE / ATC MODEL are the last two entries, STRING256 each.
        var block = data[0].Payload[28..];
        var model = block[^256..];
        Assert.Equal("SF50", System.Text.Encoding.ASCII.GetString(model[..model.IndexOf((byte)0)]));
    }

    [Fact]
    public async Task No_aircraft_data_until_xplane_reports_a_position()
    {
        var session = NewSession();
        _xp.IsConnected = false;
        await session.HandleAsync(TestPackets.AddToDataDefinition(17, "PLANE LATITUDE", "degrees"));
        await session.HandleAsync(TestPackets.RequestDataOnSimObject(1004, 17, period: 1));
        Assert.Empty(_sent);

        _xp.IsConnected = true;
        await session.HandleAsync(TestPackets.RequestDataOnSimObject(1004, 17, period: 1));
        Assert.Single(_sent);
    }

    [Fact]
    public async Task Com_set_event_writes_xplane_frequency_in_khz()
    {
        var session = NewSession();
        await session.HandleAsync(TestPackets.Open("BATCDLLSimConnection"));
        await session.HandleAsync(TestPackets.MapClientEventToSimEvent(24, "COM_RADIO_SET_HZ"));
        await session.HandleAsync(TestPackets.MapClientEventToSimEvent(22, "COM_STBY_RADIO_SET_HZ"));
        await session.HandleAsync(TestPackets.MapClientEventToSimEvent(35, "XPNDR_SET"));
        await session.HandleAsync(TestPackets.MapClientEventToSimEvent(23, "COM1_RADIO_SWAP"));

        await session.HandleAsync(TestPackets.TransmitClientEvent(0, 22, 118_850_000));
        await session.HandleAsync(TestPackets.TransmitClientEvent(0, 24, 122_250_000));
        await session.HandleAsync(TestPackets.TransmitClientEvent(0, 35, 0x4521));
        await session.HandleAsync(TestPackets.TransmitClientEvent(0, 23, 0));

        Assert.Equal(
            [
                ("sim/cockpit2/radios/actuators/com1_standby_frequency_hz_833", 118850.0),
                ("sim/cockpit2/radios/actuators/com1_frequency_hz_833", 122250.0),
                ("sim/cockpit2/radios/actuators/transponder_code", 4521.0),
            ],
            _xp.Writes.ToList());
        Assert.Equal(["sim/radios/com1_standy_flip"], _xp.Commands.ToList());
    }

    [Fact]
    public async Task Events_for_ai_aircraft_do_not_reach_xplane()
    {
        var session = NewSession();
        await session.HandleAsync(TestPackets.MapClientEventToSimEvent(24, "COM_RADIO_SET_HZ"));
        await session.HandleAsync(TestPackets.TransmitClientEvent(12345, 24, 118_850_000));

        Assert.Empty(_xp.Writes);
    }

    [Fact]
    public async Task Facility_request_without_data_gets_the_msfs_exception()
    {
        var session = NewSession();
        await session.HandleAsync(TestPackets.Open());
        await session.HandleAsync(TestPackets.RequestFacilityData(444, 9, "YSAM", sendId: 42));

        var exception = PacketDecoder.Decode(_sent[^1]);
        Assert.Equal(RecvId.Exception, (RecvId)_sent[^1].TypeField);
        Assert.Equal(1u, exception["exception"]);
        Assert.Equal(42u, exception["sendId"]);
        Assert.Equal(3u, exception["index"]);
    }

    [Fact]
    public async Task Facility_source_gets_the_clients_definition_and_ids_carry_on_across_requests()
    {
        var source = new TreeSource();
        var session = NewSession(facilities: source);
        await session.HandleAsync(TestPackets.Open());
        foreach (var name in new[] { "OPEN AIRPORT", "ICAO", "N_RUNWAYS", "OPEN RUNWAY", "PRIMARY_NUMBER", "CLOSE RUNWAY", "CLOSE AIRPORT" })
        {
            await session.HandleAsync(TestPackets.AddToFacilityDefinition(444, name));
        }

        await session.HandleAsync(TestPackets.RequestFacilityData(444, 1, "YTST"));
        await session.HandleAsync(TestPackets.RequestFacilityData(444, 2, "YTST"));

        var data = _sent.Where(p => (RecvId)p.TypeField == RecvId.FacilityData).ToList();
        Assert.Equal(6, data.Count);
        Assert.Equal([0u, 1u, 2u, 3u, 4u, 5u], data.Select(p => U32(p.Payload, 4)));
        Assert.Equal(2, _sent.Count(p => (RecvId)p.TypeField == RecvId.FacilityDataEnd));
        var definition = FacilityDefinition.FromNames(["OPEN AIRPORT", "ICAO", "N_RUNWAYS", "OPEN RUNWAY", "PRIMARY_NUMBER", "CLOSE RUNWAY", "CLOSE AIRPORT"]);
        var decoded = FacilityDecoder.Decode(definition.Root!, data.Take(3));
        Assert.Equal("YTST", decoded["ICAO"]);
        Assert.Equal([19, 29], decoded.ChildrenOf("RUNWAY").Select(r => (int)r["PRIMARY_NUMBER"]!));
    }

    [Fact]
    public async Task Facility_request_for_an_undefined_definition_reaches_the_source_without_one()
    {
        var source = new TreeSource();
        var session = NewSession(facilities: source);
        await session.HandleAsync(TestPackets.Open());
        await session.HandleAsync(TestPackets.RequestFacilityData(333, 1, "YTST", sendId: 9));

        Assert.Null(Assert.Single(source.Requests).Definition);
        Assert.Equal(RecvId.Exception, (RecvId)_sent[^1].TypeField);
    }

    [Fact]
    public async Task Changed_flag_suppresses_identical_periodic_data()
    {
        var clock = new ManualClock();
        var session = NewSession(clock);
        await session.HandleAsync(TestPackets.AddToDataDefinition(5, "PLANE LATITUDE", "degrees"));
        await session.HandleAsync(
            PacketBuilder.Client(0x0E, TestPackets.SunRise, 9).U32(7).U32(5).U32(0).U32(4).U32(1).U32(0).U32(0).U32(0).Build());

        await session.TickAsync();
        clock.Advance(TimeSpan.FromSeconds(1.1));
        await session.TickAsync();
        _xp["sim/flightmodel/position/latitude"] = -27.5;
        clock.Advance(TimeSpan.FromSeconds(1.1));
        await session.TickAsync();

        Assert.Equal(2, _sent.Count);
    }

    private static readonly string[] Define17Names =
    [
        "PLANE LATITUDE", "PLANE LONGITUDE", "INDICATED ALTITUDE CALIBRATED", "PRESSURE ALTITUDE", "PLANE ALTITUDE",
        "PLANE HEADING DEGREES TRUE", "PLANE HEADING DEGREES MAGNETIC", "AIRSPEED INDICATED", "GROUND VELOCITY",
        "GROUND ALTITUDE", "COM ACTIVE FREQUENCY:1", "COM ACTIVE FREQUENCY:2", "COM VOLUME:1", "COM VOLUME:2",
        "COM STATUS:1", "COM STATUS:2", "TRANSPONDER CODE:1", "TRANSPONDER STATE:1", "ZULU TIME", "ZULU DAY OF WEEK",
        "ZULU DAY OF YEAR", "LOCAL TIME", "CAMERA STATE", "SIM ON GROUND", "WING SPAN", "GENERAL ENG COMBUSTION:1",
        "GENERAL ENG COMBUSTION:2", "AMBIENT WIND DIRECTION", "AMBIENT WIND VELOCITY", "ABSOLUTE TIME", "COM RECEIVE:1",
        "COM RECEIVE:2", "COM TRANSMIT:1", "COM TRANSMIT:2", "TRANSPONDER IDENT:1",
    ];

    // 15 FLOAT64 + 3 FLOAT32 + 17 INT32, per the recorded definition (packed with no padding).
    private const int ExpectedDefine17Size = (15 * 8) + (3 * 4) + (17 * 4);

    private SimConnectSession NewSession(TimeProvider? time = null, IFacilitySource? facilities = null) =>
        new(1, ServerIdentity.Msfs2024, new SimVarResolver(Fixtures.Mappings(), _xp, time), _xp,
            p => { _sent.Add(p); return Task.CompletedTask; }, _log.Add, facilities, time);

    /// <summary>Answers any airport with a two-runway tree, encoded with the session's definition and encoder.</summary>
    private sealed class TreeSource : IFacilitySource
    {
        public List<FacilityRequest> Requests { get; } = [];

        public string Description => "test tree";

        public IReadOnlyList<Packet>? Find(FacilityRequest request)
        {
            Requests.Add(request);
            if (request.Definition is null)
            {
                return null;
            }

            var airport = new FacilityObject("AIRPORT").Set("ICAO", request.Icao).Set("N_RUNWAYS", 2)
                .Add(new FacilityObject("RUNWAY").Set("PRIMARY_NUMBER", 19))
                .Add(new FacilityObject("RUNWAY").Set("PRIMARY_NUMBER", 29));
            return request.Encoder.Encode(request.Definition, airport, request.RequestId);
        }
    }

    private static uint U32(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 30, 1, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}

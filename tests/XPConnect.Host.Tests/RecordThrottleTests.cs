using XPConnect.Protocol;
using XPConnect.Protocol.Tests;
using XPConnect.Recorder;

namespace XPConnect.Host.Tests;

public class RecordThrottleTests
{
    [Fact]
    public void Samples_high_rate_streams_per_object_and_keeps_everything_else()
    {
        var clock = new ManualClock();
        var throttle = new RecordThrottle(TimeSpan.FromSeconds(1), clock);
        var aiA = SetData(defineId: 5, objectId: 100);
        var aiB = SetData(defineId: 5, objectId: 101);

        Assert.True(throttle.ShouldRecord(aiA, 1));
        Assert.False(throttle.ShouldRecord(aiA, 1));
        Assert.True(throttle.ShouldRecord(aiB, 1));
        Assert.True(throttle.ShouldRecord(aiA, 2));
        Assert.True(throttle.ShouldRecord(TestPackets.AddToDataDefinition(1, "PLANE ALTITUDE", "feet"), 1));
        Assert.True(throttle.ShouldRecord(TestPackets.AddToDataDefinition(1, "PLANE ALTITUDE", "feet"), 1));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(throttle.ShouldRecord(aiA, 1));

        Assert.Equal(1, throttle.Skipped["SetDataOnSimObject"]);
    }

    [Fact]
    public void Samples_sim_object_data_replies_per_request()
    {
        var throttle = new RecordThrottle(TimeSpan.FromSeconds(1), new ManualClock());
        var data = PacketBuilder.Server(RecvId.SimObjectData, TestPackets.SunRise)
            .U32(7).U32(0).U32(1).U32(0).U32(1).U32(1).U32(1).F64(1000)
            .Build();

        Assert.True(throttle.ShouldRecord(data, 1));
        Assert.False(throttle.ShouldRecord(data, 1));
        Assert.Equal(1, throttle.Skipped["SimObjectData"]);
    }

    [Fact]
    public void Zero_interval_records_everything()
    {
        var aiA = SetData(5, 100);

        Assert.True(RecordThrottle.None.ShouldRecord(aiA, 1));
        Assert.True(RecordThrottle.None.ShouldRecord(aiA, 1));
    }

    private static Packet SetData(uint defineId, uint objectId) =>
        PacketBuilder.Client(0x10, TestPackets.SunRise, 9)
            .U32(defineId).U32(objectId).U32(0).U32(1).U32(8).F64(123.4)
            .Build();

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks = 1;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}

using ClearanceLink.Host.Pushback;

namespace ClearanceLink.Host.Tests;

public sealed class BetterPushbackTests
{
    [Theory]
    [InlineData(false, PushbackAddOn.Unknown, 0, false)] // X-Plane not connected: nothing to ask
    [InlineData(true, PushbackAddOn.Unknown, 0, true)] // just connected: ask at once
    [InlineData(true, PushbackAddOn.Installed, 29, false)]
    [InlineData(true, PushbackAddOn.Installed, 30, true)]
    [InlineData(true, PushbackAddOn.NotInstalled, 30, true)] // a plugin enabled later is found
    public void Asks_on_connect_then_every_30_seconds(bool connected, PushbackAddOn state, int seconds, bool expected) =>
        Assert.Equal(expected, BetterPushback.Due(connected, state, TimeSpan.FromSeconds(seconds)));

    [Fact]
    public async Task Finds_the_plugin_and_forgets_it_when_X_Plane_goes()
    {
        var time = new ManualTime();
        var connected = false;
        var installed = true;
        var asked = new List<string>();
        var log = new List<string>();
        var detector = new BetterPushback(() => connected, (command, _) =>
        {
            asked.Add(command);
            return Task.FromResult(installed);
        }, log.Add, time);

        await detector.CheckOnceAsync();
        Assert.Equal(PushbackAddOn.Unknown, detector.State);
        Assert.Empty(asked);

        connected = true;
        await detector.CheckOnceAsync();
        Assert.Equal(PushbackAddOn.Installed, detector.State);
        Assert.True(detector.Available);
        Assert.Equal([BetterPushback.Probe], asked);
        Assert.Single(log, l => l.StartsWith("pushback Better Pushback found", StringComparison.Ordinal));

        // Not asked again until the recheck is due.
        time.Advance(TimeSpan.FromSeconds(10));
        await detector.CheckOnceAsync();
        Assert.Single(asked);

        // Disabled in X-Plane's plugin admin: found missing at the next recheck, and nothing may be sent.
        installed = false;
        time.Advance(BetterPushback.Recheck);
        await detector.CheckOnceAsync();
        Assert.Equal(PushbackAddOn.NotInstalled, detector.State);
        Assert.False(detector.Available);
        Assert.Single(log, l => l.StartsWith("pushback Better Pushback NOT FOUND", StringComparison.Ordinal));

        // X-Plane closed: not known any more, and asked again as soon as it is back.
        connected = false;
        await detector.CheckOnceAsync();
        Assert.Equal(PushbackAddOn.Unknown, detector.State);
        connected = true;
        installed = true;
        await detector.CheckOnceAsync();
        Assert.Equal(PushbackAddOn.Installed, detector.State);
        Assert.Equal(3, asked.Count);
    }

    [Fact]
    public async Task A_failed_question_is_unknown_and_asked_again()
    {
        var fail = true;
        var log = new List<string>();
        var detector = new BetterPushback(() => true,
            (_, _) => fail ? throw new HttpRequestException("connection refused") : Task.FromResult(false), log.Add, new ManualTime());

        await detector.CheckOnceAsync();
        Assert.Equal(PushbackAddOn.Unknown, detector.State);
        Assert.Contains(log, l => l.StartsWith("pushback check failed: HttpRequestException", StringComparison.Ordinal));

        fail = false;
        await detector.CheckOnceAsync();
        Assert.Equal(PushbackAddOn.NotInstalled, detector.State);
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}

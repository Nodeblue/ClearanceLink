namespace ClearanceLink.Host.Tests;

/// <summary>The MSFS stand-in runs only while BeyondATC connects, so X-Plane add-ons that avoid MSFS can connect.</summary>
public sealed class StubLauncherTests
{
    [Fact]
    public void Nothing_happens_when_the_stand_in_was_never_started()
    {
        // No stand-in of ours running (not found, or MSFS was running at start): BeyondATC coming and going changes nothing.
        using var stub = new StubLauncher();
        var t = DateTimeOffset.UnixEpoch;
        Assert.Null(stub.FollowBatc(true, false, t));
        Assert.Null(stub.FollowBatc(true, false, t + TimeSpan.FromMinutes(1)));
        Assert.Null(stub.FollowBatc(false, false, t + TimeSpan.FromMinutes(2)));
        Assert.Equal("stub: not running", stub.Stop());
    }

    [Fact]
    public void A_missing_stand_in_is_reported_and_restart_is_not_blocked()
    {
        using var stub = new StubLauncher();
        Assert.Contains("not found", stub.Start("no-such-folder/FlightSimulator2024.exe"), StringComparison.Ordinal);
        Assert.Contains("not found", stub.Restart(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_stand_in_closes_once_beyondatc_has_settled_and_comes_back_when_it_drops()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // starts the real stand-in process (Windows only, like BeyondATC)
        }

        using var stub = new StubLauncher();
        Assert.StartsWith("stub: started", stub.Start(null), StringComparison.Ordinal);
        var t = DateTimeOffset.UtcNow;
        Assert.Null(stub.FollowBatc(true, false, t));
        Assert.Null(stub.FollowBatc(true, false, t + StubLauncher.BatcSettled / 2));
        Assert.Contains("stopped", stub.FollowBatc(true, false, t + StubLauncher.BatcSettled)!, StringComparison.Ordinal);
        Assert.Equal("stub: not needed while BeyondATC is connected", stub.Restart()); // MSFS closed meanwhile
        Assert.Contains("started", stub.FollowBatc(false, false, t + StubLauncher.BatcSettled * 3)!, StringComparison.Ordinal);
    }
}

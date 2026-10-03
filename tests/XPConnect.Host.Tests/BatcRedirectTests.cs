using XPConnect.Host;

namespace XPConnect.Host.Tests;

public sealed class BatcRedirectTests : IDisposable
{
    private readonly string _batc = Directory.CreateTempSubdirectory("xpconnect-batc").FullName;

    public BatcRedirectTests()
    {
        File.WriteAllText(Path.Combine(_batc, "BeyondATC.exe"), "");
        Directory.CreateDirectory(Path.Combine(_batc, "BeyondATC_Data", "StreamingAssets"));
    }

    [Fact]
    public void Finds_the_folder_given_only_when_it_holds_BeyondATC()
    {
        Assert.Equal(_batc, BatcRedirect.FindInstallFolder(_batc + "\\"));
        Assert.Null(BatcRedirect.FindInstallFolder(Path.Combine(_batc, "BeyondATC_Data")));
    }

    [Fact]
    public void On_writes_both_files_and_off_removes_them()
    {
        var redirect = new BatcRedirect(_batc);
        Assert.False(redirect.IsOn);

        redirect.SwitchOn();

        Assert.True(redirect.IsOn);
        Assert.All(redirect.Files, f => Assert.Contains("Port=5112", File.ReadAllText(f)));

        redirect.SwitchOff();

        Assert.All(redirect.Files, f => Assert.False(File.Exists(f)));
        Assert.All(redirect.Files, f => Assert.Equal(BatcRedirect.FileState.Missing, redirect.StateOf(f)));
    }

    [Fact]
    public void A_users_own_file_is_backed_up_and_restored()
    {
        var redirect = new BatcRedirect(_batc);
        var own = redirect.Files[1];
        File.WriteAllText(own, "[SimConnect]\nProtocol=IPv4\nAddress=192.168.1.20\nPort=500\n");
        Assert.Equal(BatcRedirect.FileState.Other, redirect.StateOf(own));

        redirect.SwitchOn();
        Assert.True(redirect.IsOn);

        redirect.SwitchOff();
        Assert.Contains("192.168.1.20", File.ReadAllText(own));
        Assert.False(File.Exists(redirect.Files[0]));
    }

    [Fact]
    public void Recognises_the_hand_written_file_from_phase_0()
    {
        // The file written by hand on Ben's PC (2026-09-30), LF line ends, no trailing newline.
        var redirect = new BatcRedirect(_batc);
        File.WriteAllText(redirect.Files[0], "[SimConnect]\nProtocol=IPv4\nAddress=127.0.0.1\nPort=5112");

        Assert.Equal(BatcRedirect.FileState.XPConnect, redirect.StateOf(redirect.Files[0]));
    }

    public void Dispose() => Directory.Delete(_batc, recursive: true);
}

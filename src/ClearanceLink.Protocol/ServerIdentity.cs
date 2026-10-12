namespace ClearanceLink.Protocol;

/// <summary>
/// What the fake server claims to be in its reply to Open. The app's helper decides which sim it is talking to
/// from the application name and version in this reply, so the values must match a real recording.
/// </summary>
public sealed record ServerIdentity(
    string ApplicationName,
    uint AppVersionMajor,
    uint AppVersionMinor,
    uint AppBuildMajor,
    uint AppBuildMinor,
    uint SimConnectVersionMajor,
    uint SimConnectVersionMinor,
    uint SimConnectBuildMajor,
    uint SimConnectBuildMinor)
{
    /// <summary>Placeholder: not yet recorded from a real MSFS 2020.</summary>
    public static readonly ServerIdentity Msfs2020 = new("KittyHawk", 11, 0, 282174, 999, 11, 0, 62651, 3);

    /// <summary>Recorded from MSFS 2024 1.8.16.0 on 2026-09-30 (docs/research/atc-app-call-inventory.md).</summary>
    public static readonly ServerIdentity Msfs2024 = new("SunRise", 12, 2, 282174, 999, 12, 2, 0, 0);

    public static ServerIdentity FromName(string name) => name.ToLowerInvariant() switch
    {
        "msfs2020" or "2020" => Msfs2020,
        "msfs2024" or "2024" => Msfs2024,
        _ => throw new ArgumentException($"Unknown identity '{name}'. Use msfs2020 or msfs2024."),
    };

    public Packet BuildOpenReply(uint protocol) =>
        PacketBuilder.Server(RecvId.Open, protocol)
            .Str(ApplicationName, PacketDecoder.NameLength)
            .U32(AppVersionMajor).U32(AppVersionMinor).U32(AppBuildMajor).U32(AppBuildMinor)
            .U32(SimConnectVersionMajor).U32(SimConnectVersionMinor).U32(SimConnectBuildMajor).U32(SimConnectBuildMinor)
            .U32(0).U32(0)
            .Build();
}

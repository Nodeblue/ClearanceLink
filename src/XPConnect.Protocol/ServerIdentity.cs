namespace XPConnect.Protocol;

/// <summary>
/// What the fake server claims to be in its reply to Open. The exact values MSFS sends are captured by a
/// pass-through recording (docs/runbooks/phase0.md); replace these placeholders with the recorded ones.
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
    // Placeholder values until a real MSFS reply is recorded.
    public static readonly ServerIdentity Msfs2020 = new("KittyHawk", 11, 0, 282174, 999, 11, 0, 62651, 3);

    public static readonly ServerIdentity Msfs2024 = new("SunRise", 12, 1, 282174, 999, 12, 1, 282174, 999);

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

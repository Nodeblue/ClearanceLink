#pragma warning disable CA1711 // Names mirror the SimConnect SDK.
namespace XPConnect.Server;

/// <summary>SIMCONNECT_PERIOD.</summary>
public enum SimConnectPeriod : uint
{
    Never = 0,
    Once = 1,
    VisualFrame = 2,
    SimFrame = 3,
    Second = 4,
}

/// <summary>SIMCONNECT_DATA_REQUEST_FLAG.</summary>
[Flags]
public enum DataRequestFlags : uint
{
    Default = 0,
    Changed = 1,
    Tagged = 2,
}

/// <summary>SIMCONNECT_EXCEPTION values we send.</summary>
public enum SimConnectException : uint
{
    None = 0,
    Error = 1,
    SizeMismatch = 2,
    UnrecognizedId = 3,
    Unopened = 4,
    VersionMismatch = 5,
    NameUnrecognized = 7,
}

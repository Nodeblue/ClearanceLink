namespace ClearanceLink.Server;

/// <summary>
/// What the SimConnect side needs from X-Plane. Dataref names may carry an array index: <c>name[2]</c>.
/// Reads are served from a cache that the implementation keeps fresh, so they never block.
/// </summary>
public interface IXPlaneData
{
    bool IsConnected { get; }

    /// <summary>Asks the implementation to start delivering this dataref. Safe to call repeatedly.</summary>
    void Watch(string dataref);

    bool TryGetNumber(string dataref, out double value);

    bool TryGetText(string dataref, out string value);

    Task WriteAsync(string dataref, double value, CancellationToken ct = default);

    Task CommandAsync(string command, CancellationToken ct = default);
}

/// <summary>X-Plane has no command by that name: the plugin that adds it isn't loaded.</summary>
public sealed class XPlaneCommandMissingException(string command) : InvalidOperationException($"X-Plane has no command {command}")
{
    public string Command => command;
}

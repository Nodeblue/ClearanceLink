using System.Collections.Concurrent;

namespace ClearanceLink.Server;

/// <summary>An X-Plane stand-in holding dataref values in memory. Used by tests; records writes and commands.</summary>
public sealed class InMemoryXPlane : IXPlaneData
{
    private readonly ConcurrentDictionary<string, double> _numbers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _texts = new(StringComparer.Ordinal);

    public bool IsConnected { get; set; } = true;

    public ConcurrentBag<string> Watched { get; } = [];

    public ConcurrentQueue<(string Dataref, double Value)> Writes { get; } = new();

    public ConcurrentQueue<string> Commands { get; } = new();

    public double this[string dataref]
    {
        set => _numbers[dataref] = value;
    }

    public void SetText(string dataref, string value) => _texts[dataref] = value;

    /// <summary>Forgets a dataref, as if X-Plane had not sent it yet.</summary>
    public bool Remove(string dataref) => _numbers.TryRemove(dataref, out _);

    public void Watch(string dataref) => Watched.Add(dataref);

    public bool TryGetNumber(string dataref, out double value) => _numbers.TryGetValue(dataref, out value);

    public bool TryGetText(string dataref, out string value) => _texts.TryGetValue(dataref, out value!);

    public Task WriteAsync(string dataref, double value, CancellationToken ct = default)
    {
        Writes.Enqueue((dataref, value));
        _numbers[dataref] = value;
        return Task.CompletedTask;
    }

    public Task CommandAsync(string command, CancellationToken ct = default)
    {
        Commands.Enqueue(command);
        return Task.CompletedTask;
    }
}

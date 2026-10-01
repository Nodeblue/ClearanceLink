namespace XPConnect.Host.Transport;

/// <summary>
/// Where to listen or connect. Syntax: <c>pipe</c> (the default MSFS pipe), <c>pipe:NAME</c>, <c>tcp:PORT</c>
/// (localhost) or <c>tcp:HOST:PORT</c>.
/// </summary>
public sealed record Endpoint(EndpointKind Kind, string PipeName = "", string Host = "127.0.0.1", int Port = 0)
{
    /// <summary>Pipe the MSFS SimConnect client library connects to when there is no SimConnect.cfg.</summary>
    public const string MsfsPipeName = @"Microsoft Flight Simulator\SimConnect";

    public static Endpoint Parse(string text)
    {
        if (text.Equals("pipe", StringComparison.OrdinalIgnoreCase))
        {
            return new Endpoint(EndpointKind.Pipe, MsfsPipeName);
        }

        if (text.StartsWith("pipe:", StringComparison.OrdinalIgnoreCase))
        {
            var name = text[5..];
            const string prefix = @"\\.\pipe\";
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[prefix.Length..];
            }

            return new Endpoint(EndpointKind.Pipe, name);
        }

        if (text.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = text[4..].Split(':');
            return parts.Length switch
            {
                1 => new Endpoint(EndpointKind.Tcp, Port: int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)),
                2 => new Endpoint(EndpointKind.Tcp, Host: parts[0], Port: int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)),
                _ => throw new FormatException($"Bad tcp endpoint '{text}'"),
            };
        }

        throw new FormatException($"Unknown endpoint '{text}'. Use pipe, pipe:NAME, tcp:PORT or tcp:HOST:PORT.");
    }

    public override string ToString() => Kind == EndpointKind.Pipe ? $@"pipe:\\.\pipe\{PipeName}" : $"tcp:{Host}:{Port}";
}

public enum EndpointKind
{
    Pipe,
    Tcp,
}

using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;

namespace ClearanceLink.Host.Transport;

/// <summary>Accepts client connections as plain streams, whatever the underlying transport.</summary>
public interface IListener : IAsyncDisposable
{
    Endpoint Endpoint { get; }

    /// <summary>Where clients can connect, for logs and the status page.</summary>
    string Description => Endpoint.ToString();

    Task<Stream> AcceptAsync(CancellationToken ct);
}

public static class Transports
{
    public static IListener Listen(Endpoint endpoint) => endpoint.Kind switch
    {
        EndpointKind.Tcp => new TcpListenerAdapter(endpoint),
        _ => new PipeListener(endpoint),
    };

    public static async Task<Stream> ConnectAsync(Endpoint endpoint, CancellationToken ct)
    {
        if (endpoint.Kind == EndpointKind.Tcp)
        {
            var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(endpoint.Host, endpoint.Port, ct).ConfigureAwait(false);
            return client.GetStream();
        }

        var pipe = new NamedPipeClientStream(".", endpoint.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(ct).ConfigureAwait(false);
        return pipe;
    }

    private sealed class TcpListenerAdapter : IListener
    {
        private readonly TcpListener _listener;

        public TcpListenerAdapter(Endpoint endpoint)
        {
            _listener = new TcpListener(IPAddress.Parse(endpoint.Host), endpoint.Port);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Endpoint = endpoint with { Port = port };
        }

        public Endpoint Endpoint { get; }

        public async Task<Stream> AcceptAsync(CancellationToken ct)
        {
            var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            client.NoDelay = true;
            return client.GetStream();
        }

        public ValueTask DisposeAsync()
        {
            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PipeListener(Endpoint endpoint) : IListener
    {
        public Endpoint Endpoint { get; } = endpoint;

        public async Task<Stream> AcceptAsync(CancellationToken ct)
        {
            // Message mode matches what native SimConnect clients may expect; it is Windows-only.
            var mode = OperatingSystem.IsWindows() ? PipeTransmissionMode.Message : PipeTransmissionMode.Byte;
            var server = new NamedPipeServerStream(
                Endpoint.PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                mode,
                PipeOptions.Asynchronous);
            try
            {
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                return server;
            }
            catch
            {
                await server.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

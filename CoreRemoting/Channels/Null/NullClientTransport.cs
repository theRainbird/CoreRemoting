using System;
using System.Threading.Tasks;

namespace CoreRemoting.Channels.Null;

/// <summary>
/// Simple in-process transport, client-side.
/// </summary>
public class NullClientTransport : NullTransport
{
    private readonly IRemotingClient _client;

    /// <summary>
    /// Creates a new instance of the <see cref="NullClientTransport"/> class
    /// without a remoting client. The URL must be set via <see cref="SetUrl"/>
    /// before connecting. No handshake metadata is sent.
    /// </summary>
    public NullClientTransport()
    {
    }

    /// <summary>
    /// Creates a new instance of the <see cref="NullClientTransport"/> class
    /// for the specified remoting client.
    /// </summary>
    /// <param name="client">CoreRemoting client</param>
    public NullClientTransport(IRemotingClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        Url = $"null://{client.Config.ServerHostName}:{client.Config.ServerPort}/rpc";
    }

    /// <summary>
    /// Sets the server URL.
    /// </summary>
    /// <param name="host">Server host</param>
    /// <param name="port">Server port</param>
    internal void SetUrl(string host, int port) =>
        Url = $"null://{host}:{port}/rpc";

    /// <summary>
    /// Gets the server URL.
    /// </summary>
    public string Url { get; private set; }

    /// <summary>
    /// Connects this endpoint to the message queue of the remote endpoint.
    /// The handshake metadata of the client is passed to the queue.
    /// </summary>
    public Task OpenAsync()
    {
        var metadata = _client?.HandshakeMessage?.Metadata;

        ThisEndpoint = NullMessageQueue.Connect(Url, metadata);
        RemoteEndpoint = Url;

        return Task.CompletedTask;
    }
}

using System;
using System.Threading.Tasks;

namespace CoreRemoting.Channels.Null;

/// <summary>
/// Simple in-process channel, client-side.
/// </summary>
public class NullClientChannel : NullTransport, IClientChannel
{
    /// <inheritdoc />
    public void Init(IRemotingClient client) =>
        SetUrl(client.Config.ServerHostName, client.Config.ServerPort);

    /// <summary>
    /// Sets the server URL.
    /// </summary>
    /// <param name="host">Server host</param>
    /// <param name="port">Server port</param>
    internal void SetUrl(string host, int port) =>
        Url = $"null://{host}:{port}/rpc";

    /// <inheritdoc />
    public string Url { get; private set; }

    /// <inheritdoc />
    public IRawMessageTransport RawMessageTransport => this;

    /// <inheritdoc />
    public async Task ConnectAsync()
    {
        ThisEndpoint = NullMessageQueue.Connect(Url);
        RemoteEndpoint = Url;

        StartListening();

        await SendMessageAsync([])
            .ConfigureAwait(false);

        OnConnected();
    }

    /// <inheritdoc />
    public override async Task DisconnectAsync()
    {
        await base.DisconnectAsync().ConfigureAwait(false);
        IsConnected = false;
        OnDisconnected();
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}

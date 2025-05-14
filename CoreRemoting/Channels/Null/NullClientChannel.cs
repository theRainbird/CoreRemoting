using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CoreRemoting.Channels.Null;

/// <summary>
/// Simple in-process channel, client-side.
/// </summary>
public class NullClientChannel : NullTransport, IClientChannel
{
    /// <inheritdoc />
    public void Init(IRemotingClient client)
    {
        RemotingClient = client ?? throw new ArgumentNullException(nameof(client));
        SetUrl(client.Config.ServerHostName, client.Config.ServerPort);
    }

    /// <summary>
    /// Sets the server URL.
    /// </summary>
    /// <param name="host">Server host</param>
    /// <param name="port">Server port</param>
    internal void SetUrl(string host, int port) =>
        Url = $"null://{host}:{port}/rpc";

    /// <inheritdoc />
    public string Url { get; private set; }

    private IRemotingClient RemotingClient { get; set; }

    /// <inheritdoc />
    public IRawMessageTransport RawMessageTransport => this;

    /// <inheritdoc />
    public Task ConnectAsync()
    {
        var metadata = Array.Empty<string>();
        if (RemotingClient?.MessageEncryption ?? false)
        {
            metadata = [nameof(RemotingClient.PublicKey),
                Convert.ToBase64String(RemotingClient.PublicKey)];
        }

        ThisEndpoint = NullMessageQueue.Connect(Url, metadata);
        RemoteEndpoint = Url;

        StartListening();
        OnConnected();

        return Task.CompletedTask;
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

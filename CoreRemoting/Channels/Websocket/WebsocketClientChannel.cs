using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace CoreRemoting.Channels.Websocket;

/// <summary>
/// Client side websocket channel implementation based on System.Net.Websockets.
/// </summary>
public class WebsocketClientChannel : WebsocketTransport, IClientChannel
{
    private IRemotingClient _client;

    /// <summary>
    /// Gets or sets the URL this channel is connected to.
    /// </summary>
    public string Url { get; private set; }

    private Uri Uri { get; set; }

    /// <inheritdoc />
    public bool IsConnected { get; private set; }

    /// <inheritdoc />
    public IRawMessageTransport RawMessageTransport => this;

    private ClientWebSocket ClientWebSocket { get; set; }

    /// <inheritdoc />
    protected override WebSocket WebSocket => ClientWebSocket;

    /// <inheritdoc />
    public void Init(IRemotingClient client)
    {
        _client = client;

        Url =
            "ws://" +
            client.Config.ServerHostName + ":" +
            Convert.ToString(client.Config.ServerPort) +
            "/rpc";

        Uri = new Uri(Url);

        // note: Nagle is disabled by default on NetCore, see
        // https://github.com/dotnet/runtime/discussions/81175
        ClientWebSocket = new ClientWebSocket();
        ClientWebSocket.Options.Cookies = new CookieContainer();

        // send handshake metadata items as cookies
        foreach (var kv in client.HandshakeMessage.Metadata)
        {
            ClientWebSocket.Options.Cookies.Add(new Cookie(
                name: kv.Key,
                value: kv.Value,
                path: Uri.LocalPath,
                domain: Uri.Host));
        }
    }

    /// <inheritdoc />
    public async Task ConnectAsync()
    {
        // ClientWebSocket cannot be reused after it enters the Closed state.
        // If it's not in the pristine None state, create a fresh instance.
        if (ClientWebSocket.State != WebSocketState.None && _client != null)
        {
            TryDispose(ClientWebSocket);
            Init(_client);
        }

        // reset stale error from a previous connection
        LastException = null;

        await ClientWebSocket.ConnectAsync(
            new Uri(Url), CancellationToken.None)
                .ConfigureAwait(false);

        IsConnected = true;

        await StartListening()
            .ConfigureAwait(false);

        await SendMessageAsync([])
            .ConfigureAwait(false);

        OnConnected();
    }

    /// <inheritdoc />
    public override async Task DisconnectAsync()
    {
        if (!IsConnected)
            return;

        IsConnected = false;

        // send close frame to the server,
        // the read loop receives the server's close reply, sends ours back,
        // and exits on its own; await it for deterministic teardown
        await base.DisconnectAsync()
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (ClientWebSocket == null)
            return;

        if (IsConnected)
            await DisconnectAsync()
                .ConfigureAwait(false);

        ClientWebSocket.Dispose();
        ClientWebSocket = null;

        await base.DisposeAsync()
            .ConfigureAwait(false);
    }
}

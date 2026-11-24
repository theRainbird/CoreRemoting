using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Toolbox;

namespace CoreRemoting.Channels.Websocket;

/// <summary>
/// Websocket client transport, based on <see cref="ClientWebSocket"/>.
/// Owns the socket lifecycle: a fresh socket is created on each connect,
/// because a <see cref="ClientWebSocket"/> cannot be reused after being closed.
/// Handshake metadata is carried as cookies; the handshake message itself
/// is an empty byte message.
/// </summary>
public class WebsocketClientTransport : WebsocketTransport
{
    private readonly IRemotingClient _client;

    /// <summary>
    /// Creates a new instance of the <see cref="WebsocketClientTransport"/> class.
    /// </summary>
    /// <param name="client">CoreRemoting client</param>
    public WebsocketClientTransport(IRemotingClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));

        Url = "ws://" + client.Config.ServerHostName + ":" + Convert.ToString(client.Config.ServerPort) + "/rpc";
        Uri = new Uri(Url);
    }

    /// <summary>
    /// The URL of the remote endpoint.
    /// </summary>
    public string Url { get; private set; }

    private Uri Uri { get; set; }

    private ClientWebSocket ClientWebSocket { get; set; }

    /// <inheritdoc />
    protected override WebSocket WebSocket => ClientWebSocket;

    /// <summary>
    /// Creates a fresh client websocket and connects it to the server.
    /// A <see cref="ClientWebSocket"/> cannot be reused after Closed state,
    /// so a new instance is created on every connect.
    /// </summary>
    public async Task OpenAsync()
    {
        ClientWebSocket = new ClientWebSocket();
        ClientWebSocket.Options.Cookies = new CookieContainer();

        foreach (var kv in _client.HandshakeMessage.Metadata)
        {
            ClientWebSocket.Options.Cookies.Add(
                new Cookie(name: kv.Key, value: kv.Value, path: Uri.LocalPath, domain: Uri.Host));
        }

        LastException = null;

        await ClientWebSocket
            .ConnectAsync(new Uri(Url), CancellationToken.None)
            .ConfigureAwait(false);

        IsConnectedFlag = true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Sends a close frame. The read loop receives the server's close reply,
    /// answers with our own close frame and exits on its own.
    /// Awaiting the loop here provides a deterministic teardown.
    /// </remarks>
    public override async Task DisconnectAsync()
    {
        if (!IsConnected)
            return;

        IsConnectedFlag = false;

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

        ClientWebSocket.TryDispose();
        ClientWebSocket = null;

        await base.DisposeAsync()
            .ConfigureAwait(false);
    }
}

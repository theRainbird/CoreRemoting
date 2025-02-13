using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Toolbox;

namespace CoreRemoting.Channels.Websocket;

/// <summary>
/// Client side websocket channel implementation based on System.Net.Websockets.
/// </summary>
public class WebsocketClientChannel : WebSocketTransport, IClientChannel
{
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
        ClientWebSocket.Options.Cookies.Add(new Cookie(
            name: MessageEncryptionCookie,
            value: client.MessageEncryption ? "1" : "0",
            path: Uri.LocalPath,
            domain: Uri.Host));

        if (client.MessageEncryption)
        {
            ClientWebSocket.Options.Cookies.Add(new Cookie(
                name: ClientPublicKeyCookie,
                value: Convert.ToBase64String(client.PublicKey),
                path: Uri.LocalPath,
                domain: Uri.Host));
        }
    }

    /// <inheritdoc />
    public async Task ConnectAsync()
    {
        await ClientWebSocket.ConnectAsync(
            new Uri(Url), CancellationToken.None)
                .ConfigureAwait(false);

        IsConnected = true;
        StartListening();

        await WebSocket.SendAsync(EmptyMessage,
            WebSocketMessageType.Binary, true, CancellationToken.None)
                .ConfigureAwait(false);

        OnConnected();
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        if (!IsConnected)
            return;

        IsConnected = false;

        try
        {
            await ClientWebSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure, "Ok", CancellationToken.None)
                    .ConfigureAwait(false);
        }
        catch // (ObjectDisposedException)
        {
            // web socket already closed?
        }

        OnDisconnected();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (ClientWebSocket == null)
            return;

        if (IsConnected)
            DisconnectAsync()
                .JustWait();

        ClientWebSocket.Dispose();
        ClientWebSocket = null;
    }
}

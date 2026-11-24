using System;
using System.Threading.Tasks;
using CoreRemoting.Channels.Common;
using WebSocketSharp;
using WebSocketSharp.Net;

namespace CoreRemoting.Channels.WebsocketSharp;

/// <summary>
/// Client side websocket transport based on websocket-sharp.
/// Owns the <see cref="WebSocket"/> instance, the connect mechanics
/// and the event wiring.
/// </summary>
public class WebsocketSharpClientTransport : MessageTransportBase, IAsyncDisposable
{
    private WebSocket _webSocket;

    /// <summary>
    /// Creates a new instance of the <see cref="WebsocketSharpClientTransport"/> class.
    /// </summary>
    /// <param name="client">CoreRemoting client</param>
    public WebsocketSharpClientTransport(IRemotingClient client)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        string url =
            "ws://" +
            client.Config.ServerHostName + ":" +
            Convert.ToString(client.Config.ServerPort) +
            "/rpc";

        _webSocket = new WebSocket(url) { NoDelay = true };

        // send handshake metadata items as cookies
        foreach (var kv in client.HandshakeMessage.Metadata)
        {
            _webSocket.SetCookie(new Cookie(
                name: kv.Key,
                value: kv.Value));
        }

        _webSocket.Log.Output = (timestamp, text) => Console.WriteLine("{0}: {1}", timestamp, text);
        _webSocket.Log.Level = LogLevel.Debug;
    }

    /// <summary>
    /// Establishes the websocket connection.
    /// </summary>
    public void Open()
    {
        LastException = null;

        // The WebSocket instance is reused across reconnects, so make the event wiring
        // idempotent: detach any handlers left over from a previous connection before
        // attaching them again. Without this, every reconnect would add another
        // subscription and deliver each received message multiple times.
        _webSocket.OnMessage -= OnMessage;
        _webSocket.OnError -= OnError;
        _webSocket.OnClose -= OnServerDisconnected;

        _webSocket.OnMessage += OnMessage;
        _webSocket.OnError += OnError;
        // OnClose is intentionally attached only after the new connection is
        // fully established: a late close event from the previous connection
        // must not reach the client while the new connection is being set up.

        _webSocket.Connect();
    }

    /// <summary>
    /// Sends the empty handshake message and attaches the disconnect handler.
    /// </summary>
    public void SendHandshake()
    {
        _webSocket.Send(string.Empty);

        _webSocket.OnClose += OnServerDisconnected;
    }

    private void OnServerDisconnected(object o, CloseEventArgs closeEventArgs)
    {
        OnDisconnected();
    }

    /// <summary>
    /// Event procedure: Called when a error occurs on the websocket layer.
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="e">Event arguments</param>
    private void OnError(object sender, ErrorEventArgs e)
    {
        LastException = new NetworkException(e.Message, e.Exception);

        OnErrorOccured(e.Message, e.Exception);
    }

    /// <summary>
    /// Event procedure: Called when a message from server is received.
    /// </summary>
    /// <param name="sender">Sender of the event</param>
    /// <param name="e">Event arguments containing the message content</param>
    private void OnMessage(object sender, MessageEventArgs e)
    {
        OnReceiveMessage(e.RawData);
    }

    /// <summary>
    /// Closes the websocket connection.
    /// </summary>
    public Task DisconnectAsync()
    {
        if (_webSocket == null)
            return Task.CompletedTask;

        _webSocket.OnMessage -= OnMessage;
        _webSocket.OnError -= OnError;

        _webSocket.Close(CloseStatusCode.Normal);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets whether the websocket connection is established or not.
    /// </summary>
    public bool IsConnected
    {
        get
        {
            if (_webSocket == null)
                throw new InvalidOperationException("Channel is not initialized.");

            return _webSocket.IsAlive;
        }
    }

    /// <inheritdoc />
    protected override Task<bool> SendCoreAsync(byte[] rawMessage)
    {
        _webSocket.Send(rawMessage);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Disconnect and free manages resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _webSocket = null;
    }
}

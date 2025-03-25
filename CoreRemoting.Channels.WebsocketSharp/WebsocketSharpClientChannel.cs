using System;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Net;

namespace CoreRemoting.Channels.WebsocketSharp;

/// <summary>
/// Client side websocket channel implementation based on websocket-sharp.
/// </summary>
public class WebsocketSharpClientChannel : IClientChannel, IRawMessageTransport
{
    private WebSocket _webSocket;

    /// <summary>
    /// Event: Fires when a message is received from server.
    /// </summary>
    public event Action<byte[]> ReceiveMessage;

    /// <summary>
    /// Event: Fires when an error is occurred.
    /// </summary>
    public event Action<string, Exception> ErrorOccured;

    /// <inheritdoc />
    public event Action Disconnected;

    /// <summary>
    /// Initializes the channel.
    /// </summary>
    /// <param name="client">CoreRemoting client</param>
    public void Init(IRemotingClient client)
    {
        string url =
            "ws://" +
            client.Config.ServerHostName + ":" +
            Convert.ToString(client.Config.ServerPort) +
            "/rpc";

        _webSocket = new WebSocket(url) { NoDelay = true };

        _webSocket.SetCookie(new Cookie(
            name: "MessageEncryption",
            value: client.MessageEncryption ? "1" : "0"));

        if (client.MessageEncryption)
        {
            _webSocket.SetCookie(new Cookie(
                "ShakeHands",
                Convert.ToBase64String(client.PublicKey)));
        }

        _webSocket.Log.Output = (timestamp, text) => Console.WriteLine("{0}: {1}", timestamp, text);
        _webSocket.Log.Level = LogLevel.Debug;
    }

    /// <summary>
    /// Establish a websocket connection with the server.
    /// </summary>
    public Task ConnectAsync()
    {
        if (_webSocket == null)
            throw new InvalidOperationException("Channel is not initialized.");

        if (_webSocket.IsAlive)
            return Task.CompletedTask;

        LastException = null;

        _webSocket.OnMessage += OnMessage;
        _webSocket.OnError += OnError;
        _webSocket.OnClose += OnDisconnected;

        _webSocket.Connect();
        _webSocket.Send(string.Empty);
        return Task.CompletedTask;
    }

    private void OnDisconnected(object o, CloseEventArgs closeEventArgs)
    {
        Disconnected?.Invoke();
    }

    /// <summary>
    /// Event procedure: Called when a error occurs on the websocket layer.
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="e">Event arguments</param>
    private void OnError(object sender, ErrorEventArgs e)
    {
        LastException = new NetworkException(e.Message, e.Exception);

        ErrorOccured?.Invoke(e.Message, e.Exception);
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

    /// <summary>
    /// Gets the raw message transport component for this connection.
    /// </summary>
    public IRawMessageTransport RawMessageTransport => this;

    /// <summary>
    /// Disconnect and free manages resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _webSocket = null;
    }

    /// <summary>
    /// Event procedure: Called when a message from server is received.
    /// </summary>
    /// <param name="sender">Sender of the event</param>
    /// <param name="e">Event arguments containing the message content</param>
    private void OnMessage(object sender, MessageEventArgs e)
    {
        ReceiveMessage?.Invoke(e.RawData);
    }

    /// <summary>
    /// Sends a message to the server.
    /// </summary>
    /// <param name="rawMessage">Raw message data</param>
    public Task<bool> SendMessageAsync(byte[] rawMessage)
    {
        _webSocket.Send(rawMessage);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Gets or sets the last exception.
    /// </summary>
    public NetworkException LastException { get; set; }
}
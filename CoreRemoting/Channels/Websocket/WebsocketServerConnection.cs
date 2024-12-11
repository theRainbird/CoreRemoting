using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.IO;

namespace CoreRemoting.Channels.Websocket;

/// <summary>
/// Websocket connection.
/// </summary>
public class WebsocketServerConnection : IRawMessageTransport
{
    // note: LOH threshold is ~85 kilobytes
    private const int BufferSize = 16 * 1024;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebsocketServerConnection"/> class.
    /// </summary>
    public WebsocketServerConnection(string clientAddress, HttpListenerWebSocketContext websocketContext, WebSocket websocket, IRemotingServer remotingServer)
    {
        ClientAddress = clientAddress ?? throw new ArgumentNullException(nameof(clientAddress));
        WebSocketContext = websocketContext ?? throw new ArgumentNullException(nameof(websocketContext));
        WebSocket = websocket ?? throw new ArgumentNullException(nameof(websocket));
        RemotingServer = remotingServer ?? throw new ArgumentNullException(nameof(remotingServer));
    }

    private string ClientAddress { get; set; }

    private HttpListenerWebSocketContext WebSocketContext { get; set; }

    private WebSocket WebSocket { get; set; }

    private IRemotingServer RemotingServer { get; set; }

    private RemotingSession Session { get; set; }

    /// <inheritdoc/>
    public NetworkException LastException { get; set; }

    /// <inheritdoc/>
    public event Action<byte[]> ReceiveMessage;

    /// <inheritdoc/>
    public event Action<string, Exception> ErrorOccured;

    /// <summary>
    /// Event: fires when a web socket is disconnected.
    /// </summary>
    public event Action Disconnected;

    /// <inheritdoc/>
    public async Task<bool> SendMessageAsync(byte[] rawMessage)
    {
        try
        {
            await WebSocket.SendAsync(
                new ArraySegment<byte>(rawMessage), 
                    WebSocketMessageType.Binary, true, CancellationToken.None)
                        .ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            LastException = ex as NetworkException ??
                new NetworkException(ex.Message, ex);

            ErrorOccured?.Invoke(ex.Message, LastException);
            return false;
        }
    }

    /// <summary>
    /// Starts listening to the incoming messages.
    /// </summary>
    public Guid StartListening()
    {
        var sessionId = CreateRemotingSession();
        _ = ReadIncomingMessages();
        return sessionId;
    }

    /// <summary>
    /// Creates <see cref="RemotingSession"/> for the incoming websocket connection.
    /// </summary>
    private Guid CreateRemotingSession()
    {
        byte[] clientPublicKey = null;

        var messageEncryptionCookie = WebSocketContext.CookieCollection["MessageEncryption"];
        if (messageEncryptionCookie?.Value == "1")
        {
            var shakeHandsCookie = WebSocketContext.CookieCollection["ShakeHands"];
            clientPublicKey =
                Convert.FromBase64String(
                    shakeHandsCookie.Value);
        }

        Session = RemotingServer.SessionRepository.CreateSession(
            clientPublicKey, ClientAddress, RemotingServer, this);

        return Session.SessionId;
    }

    private async Task ReadIncomingMessages()
    {
        var buffer = new byte[BufferSize];
        var segment = new ArraySegment<byte>(buffer);
        var webSocket = WebSocket;

        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var ms = new SmallBlockMemoryStream();
                while (true)
                {
                    var result = await webSocket.ReceiveAsync(segment,
                        CancellationToken.None).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                            string.Empty, CancellationToken.None).ConfigureAwait(false);

                        Disconnected?.Invoke();
                    }
                    else
                    {
                        ms.Write(buffer, 0, result.Count);
                    }

                    if (result.EndOfMessage)
                        break;
                }

                if (ms.Length > 0)
                {
                    // flush received websocket message
                    var message = new byte[(int)ms.Length];
                    ms.Position = 0;
                    ms.Read(message, 0, message.Length);
                    ReceiveMessage?.Invoke(message);
                }
            }
        }
        catch (Exception ex)
        {
            LastException = ex as NetworkException ??
                new NetworkException(ex.Message, ex);

            ErrorOccured?.Invoke(ex.Message, LastException);
            Disconnected?.Invoke();
        }
        finally
        {
            webSocket?.Dispose();
        }
    }
}

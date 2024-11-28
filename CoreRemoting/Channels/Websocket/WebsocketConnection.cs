using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.IO;

namespace CoreRemoting.Channels.Websocket;

/// <summary>
/// Websocket connection.
/// </summary>
public class WebsocketConnection : IRawMessageTransport
{
    // note: LOH threshold is ~85 kilobytes
    private const int BufferSize = 16 * 1024;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebsocketConnection"/> class.
    /// </summary>
    public WebsocketConnection(HttpListenerWebSocketContext websocketContext, WebSocket websocket, IRemotingServer remotingServer)
    {
        WebSocketContext = websocketContext ?? throw new ArgumentNullException(nameof(websocketContext));
        WebSocket = websocket ?? throw new ArgumentNullException(nameof(websocket));
        RemotingServer = remotingServer ?? throw new ArgumentNullException(nameof(remotingServer));
    }

    public Guid Guid { get; } = Guid.NewGuid();

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
    public bool SendMessage(byte[] rawMessage)
    {
        try
        {
            var segment = new ArraySegment<byte>(rawMessage);
            WebSocket.SendAsync(segment, WebSocketMessageType.Binary, true, CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

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
    public void StartListening()
    {
        CreateRemotingSession();
        _ = ReadIncomingMessages();
    }

    /// <summary>
    /// Creates <see cref="RemotingSession"/> for the incoming websocket connection.
    /// </summary>
    private void CreateRemotingSession()
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
            clientPublicKey, RemotingServer, this);
    }

    private async Task ReadIncomingMessages()
    {
        var buffer = new byte[BufferSize];
        var segment = new ArraySegment<byte>(buffer);

        try
        {
            while (WebSocket.State == WebSocketState.Open)
            {
                var ms = new SmallBlockMemoryStream();
                while (true)
                {
                    var result = await WebSocket.ReceiveAsync(segment,
                        CancellationToken.None).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure,
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
                    ReceiveMessage(message);
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
            WebSocket?.Dispose();
        }
    }
}

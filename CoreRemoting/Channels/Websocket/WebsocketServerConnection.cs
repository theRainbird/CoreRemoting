using System;
using System.Net.WebSockets;

namespace CoreRemoting.Channels.Websocket;

/// <summary>
/// Websocket connection.
/// </summary>
public class WebsocketServerConnection : WebSocketTransport, IDisposable
{
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

    /// <inheritdoc/>
    protected override WebSocket WebSocket { get; }

    private IRemotingServer RemotingServer { get; set; }

    private RemotingSession Session { get; set; }

    /// <summary>
    /// Starts listening to the incoming messages.
    /// </summary>
    public override Guid StartListening()
    {
        var sessionId = CreateRemotingSession();
        base.StartListening();
        return sessionId;
    }

    /// <summary>
    /// Creates <see cref="RemotingSession"/> for the incoming websocket connection.
    /// </summary>
    private Guid CreateRemotingSession()
    {
        byte[] clientPublicKey = null;

        var cookies = WebSocketContext.CookieCollection;
        var messageEncryptionCookie = cookies[MessageEncryptionCookie];
        if (messageEncryptionCookie?.Value == "1")
        {
            var shakeHandsCookie = cookies[ClientPublicKeyCookie];
            clientPublicKey =
                Convert.FromBase64String(
                    shakeHandsCookie.Value);
        }

        Session = RemotingServer.SessionRepository.CreateSession(
            clientPublicKey, ClientAddress, RemotingServer, this);

        return Session.SessionId;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        WebSocket.Dispose();

        base.Dispose();
    }
}

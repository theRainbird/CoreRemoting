using System;
using System.Net.WebSockets;
using System.Threading.Tasks;
using CoreRemoting.Toolbox;

namespace CoreRemoting.Channels.Websocket;

/// <summary>
/// Websocket connection.
/// </summary>
public class WebsocketServerConnection : WebsocketTransport, IAsyncDisposable
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
    public override async Task<Guid> StartListening()
    {
        var sessionId = await CreateRemotingSession().ConfigureAwait(false);
        await base.StartListening().ConfigureAwait(false);
        return sessionId;
    }

    /// <summary>
    /// Creates <see cref="RemotingSession"/> for the incoming websocket connection.
    /// </summary>
    private async Task<Guid> CreateRemotingSession()
    {
        byte[] clientPublicKey = null;
        Guid? resumableSessionId = null;

        var cookies = WebSocketContext.CookieCollection;
        var messageEncryptionCookie = cookies[MessageEncryptionCookie];
        var messageEncryptionEnabled = messageEncryptionCookie?.Value == "1";
        if (messageEncryptionEnabled)
        {
            var shakeHandsCookie = cookies[ClientPublicKeyCookie];
            clientPublicKey =
                Convert.FromBase64String(
                    shakeHandsCookie.Value);

            var resumeSessionIdCookie = cookies[ResumeSessionIdCookie];
            if (resumeSessionIdCookie != null)
                resumableSessionId = new Guid(Convert.FromBase64String(resumeSessionIdCookie.Value));
        }

        Session = await RemotingServer.SessionRepository.ResumeOrCreateSession(
            resumableSessionId, messageEncryptionEnabled, clientPublicKey, ClientAddress, RemotingServer, this)
                .ConfigureAwait(false);

        return Session.SessionId;
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        WebSocket.Dispose();

        await base.DisposeAsync()
            .ConfigureAwait(false);
    }
}

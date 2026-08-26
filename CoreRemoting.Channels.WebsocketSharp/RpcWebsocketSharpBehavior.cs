using System;
using System.Threading.Tasks;
using CoreRemoting.Toolbox;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace CoreRemoting.Channels.WebsocketSharp;

/// <summary>
/// Executes RPC calls from clients.
/// </summary>
public class RpcWebsocketSharpBehavior : WebSocketBehavior, IRawMessageTransport, IDisposable
{
    private IRemotingServer _server;
    private RemotingSession _session;

    /// <summary>
    /// Event: Fired when a message is received via websocket.
    /// </summary>
    public event Action<byte[]> ReceiveMessage;

    /// <summary>
    /// Event: Fires when an error is occurred.
    /// </summary>
    public event Action<string, Exception> ErrorOccured;

    /// <summary>
    /// Event: Fires when the websocket connection has been closed.
    /// </summary>
    public event Action Disconnected;

    /// <summary>
    /// Initializes the RPC service instance.
    /// </summary>
    /// <param name="server">Remoting server instance, which is hosting the service to call</param>
    public RpcWebsocketSharpBehavior(IRemotingServer server)
    {
        _server = server ?? throw new ArgumentException(nameof(server));
    }

    /// <summary>
    /// Sends a message over the websocket.
    /// </summary>
    /// <param name="rawMessage">Raw data of the message</param>
    public Task<bool> SendMessageAsync(byte[] rawMessage)
    {
        Send(rawMessage);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Called when a message from a client is received.
    /// </summary>
    /// <param name="e">Metadata and the message from client</param>
    protected override void OnMessage(MessageEventArgs e)
    {
        if (_session == null)
        {
            _session = CreateOrResumeRemotingSession();

            _session.BeforeDispose += BeforeDisposeSession;
        }
        else
            ReceiveMessage?.Invoke(e.RawData);
    }

    /// <summary>
    /// Creates a new remoting session for this connection or resumes an existing parked session,
    /// if the client presented a resumable session ID whose public key matches.
    /// </summary>
    /// <returns>The newly created or resumed session</returns>
    private RemotingSession CreateOrResumeRemotingSession()
    {
        byte[] clientPublicKey = null;
        Guid? resumableSessionId = null;

        var messageEncryptionCookie = Context.CookieCollection["MessageEncryption"];
        var messageEncryptionEnabled = messageEncryptionCookie?.Value == "1";
        if (messageEncryptionEnabled)
        {
            var shakeHandsCookie = Context.CookieCollection["ShakeHands"];

            clientPublicKey =
                Convert.FromBase64String(
                    shakeHandsCookie.Value);

            var resumeSessionIdCookie = Context.CookieCollection["ResumeSessionId"];
            if (resumeSessionIdCookie != null)
                resumableSessionId = new Guid(Convert.FromBase64String(resumeSessionIdCookie.Value));
        }

        return _server.SessionRepository.ResumeOrCreateSession(
            resumableSessionId, messageEncryptionEnabled, clientPublicKey,
            Context.UserEndPoint.ToString(), _server, this)
                .GetAwaiter().GetResult();
    }

    /// <summary>
    /// Event procedure: Called when the websocket connection has been closed.
    /// </summary>
    /// <param name="e">CloseEventArgs</param>
    protected override void OnClose(CloseEventArgs e) =>
        Disconnected?.Invoke();

    /// <summary>
    /// Closes the internal websocket session.
    /// </summary>
    private void BeforeDisposeSession()
    {
        _session = null;
        Sessions.CloseSession(ID);
    }

    /// <summary>
    /// Event procedure: Called, if an error occurs at the websocket layer.
    /// </summary>
    /// <param name="e">Message and optional Exception info</param>
    protected override void OnError(ErrorEventArgs e)
    {
        LastException = new NetworkException(e.Message, e.Exception);

        ErrorOccured?.Invoke(e.Message, e.Exception);
    }

    /// <summary>
    /// Frees managed resources.
    /// </summary>
    public void Dispose()
    {
        _server = null;
    }

    /// <summary>
    /// Gets or sets the last exception.
    /// </summary>
    public NetworkException LastException { get; set; }
}
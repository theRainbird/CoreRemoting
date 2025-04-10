using System;
using CoreRemoting.Channels.Null;

namespace CoreRemoting.Channels.Websocket;

/// <summary>
/// Websocket connection.
/// </summary>
public class NullServerConnection : NullTransport, IAsyncDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NullServerConnection"/> class.
    /// </summary>
    public NullServerConnection(NullMessage connectionMessage, IRemotingServer remotingServer)
    {
        ConnectionMessage = connectionMessage ?? throw new ArgumentNullException(nameof(connectionMessage));
        ClientAddress = connectionMessage.Sender ?? throw new ArgumentNullException(nameof(connectionMessage.Sender));
        RemotingServer = remotingServer; // ?? throw new ArgumentNullException(nameof(remotingServer));
    }

    private string ClientAddress { get; set; }

    private NullMessage ConnectionMessage { get; set; }

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

        // get encryption metadata from NullMessage
        //var cookies = WebSocketContext.CookieCollection;
        //var messageEncryptionCookie = cookies[MessageEncryptionCookie];
        //if (messageEncryptionCookie?.Value == "1")
        //{
        //    var shakeHandsCookie = cookies[ClientPublicKeyCookie];
        //    clientPublicKey =
        //        Convert.FromBase64String(
        //            shakeHandsCookie.Value);
        //}

        if (RemotingServer != null)
        {
            Session = RemotingServer.SessionRepository.CreateSession(
                clientPublicKey, ClientAddress, RemotingServer, this);

            return Session.SessionId;
        }

        return Guid.NewGuid();
    }
}

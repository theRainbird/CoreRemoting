using System;
using System.Net.Quic;
using System.Text;
using System.Threading.Tasks;
using CoreRemoting.Toolbox;

namespace CoreRemoting.Channels.Quic;

/// <summary>
/// Server-side QUIC channel connection, based on System.Net.Quic.
/// </summary>
public class QuicServerConnection : QuicTransport, IRawMessageTransport
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QuicServerConnection"/> class.
    /// </summary>
    public QuicServerConnection(QuicConnection connection, QuicStream stream, IRemotingServer remotingServer)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        ClientStream = stream ?? throw new ArgumentNullException(nameof(stream));
        RemotingServer = remotingServer ?? throw new ArgumentNullException(nameof(remotingServer));
        ClientReader = new(stream, Encoding.UTF8, true);
        ClientWriter = new(stream, Encoding.UTF8, true);
        IsConnected = true;
    }

    private IRemotingServer RemotingServer { get; set; }

    private RemotingSession Session { get; set; }

    /// <summary>
    /// Starts listening to the incoming messages.
    /// </summary>
    public override async Task<Guid> StartListening()
    {
        var sessionId = await CreateRemotingSession()
            .ConfigureAwait(false);

        await base.StartListening()
            .ConfigureAwait(false);

        return sessionId;
    }

    /// <summary>
    /// Creates <see cref="RemotingSession"/> for the incoming QUIC connection.
    /// </summary>
    private async Task<Guid> CreateRemotingSession()
    {
        // read handshake message
        var handshakeMessage = await ReadIncomingMessage()
            .ConfigureAwait(false);

        var messageEncryption = false;
        Guid? resumableSessionId = null;
        byte[] clientPublicKey = handshakeMessage;

        if (handshakeMessage is not null)
        {
            var handshake = QuicHandshakeMessage.FromByteArray(handshakeMessage);
            messageEncryption = handshake.MessageEncryption;
            resumableSessionId = handshake.ResumableSessionId;
            clientPublicKey = handshake.ClientPublicKey;
        }

        // disable message encryption if public key is empty
        if (clientPublicKey != null && clientPublicKey.Length == 0)
            clientPublicKey = null;

        Session =
            await RemotingServer.SessionRepository.ResumeOrCreateSession(
                resumableSessionId, messageEncryption, clientPublicKey,
                Connection.RemoteEndPoint.ToString(), RemotingServer, this)
                    .ConfigureAwait(false);

        return Session.SessionId;
    }
}

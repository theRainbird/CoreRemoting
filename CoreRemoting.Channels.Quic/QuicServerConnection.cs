using System;
using System.Net.Quic;
using System.Text;
using System.Threading.Tasks;

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

        Guid? resumableSessionId = null;
        byte[] clientPublicKey = handshakeMessage;

        // resume format: [0x01][session ID (16 bytes)][client public key]
        // (a CSP key blob starts with 0x06 and an empty handshake is length 0,
        // so the version prefix cannot collide)
        if (handshakeMessage != null && handshakeMessage.Length > 17 && handshakeMessage[0] == 0x01)
        {
            resumableSessionId = new Guid(new ReadOnlySpan<byte>(handshakeMessage, 1, 16).ToArray());

            var keyLength = handshakeMessage.Length - 17;
            clientPublicKey = new byte[keyLength];
            Array.Copy(handshakeMessage, 17, clientPublicKey, 0, keyLength);
        }

        // disable message encryption if handshake is empty
        if (clientPublicKey != null && clientPublicKey.Length == 0)
            clientPublicKey = null;

        Session =
            (await RemotingServer.SessionRepository.TryResumeSession(
                resumableSessionId ?? Guid.Empty,
                clientPublicKey,
                this)
                    .ConfigureAwait(false))
            ?? await RemotingServer.SessionRepository.CreateSession(
                clientPublicKey, Connection.RemoteEndPoint.ToString(),
                    RemotingServer, this)
                        .ConfigureAwait(false);

        return Session.SessionId;
    }
}

using System;
using System.Net.Quic;
using System.Text;
using System.Threading.Tasks;
using CoreRemoting.RpcMessaging;
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
        var handshakeBytes = await ReadIncomingMessage()
            .ConfigureAwait(false);

        // deserialize handshake and add client address
        var serializer = RemotingServer.Serializer;
        var handshakeMessage = serializer.Deserialize<ClientHandshakeMessage>(handshakeBytes);
        handshakeMessage.ClientAddress = Connection.RemoteEndPoint.ToString();

        Session = await RemotingServer.SessionRepository
            .ResumeOrCreateSession(handshakeMessage, RemotingServer, this)
                .ConfigureAwait(false);

        return Session.SessionId;
    }
}

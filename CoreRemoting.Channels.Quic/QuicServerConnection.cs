using System;
using System.Net.Quic;
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
        IsConnected = true;
    }

    private IRemotingServer RemotingServer { get; set; }

    private RemotingSession Session { get; set; }

    /// <summary>
    /// Starts listening to the incoming messages.
    /// </summary>
    public override async Task<Guid> StartListening()
    {
        try
        {
            var sessionId = await CreateRemotingSession()
                .ConfigureAwait(false);

            await base.StartListening()
                .ConfigureAwait(false);

            return sessionId;
        }
        catch
        {
            IsConnected = false;

            if (ClientStream != null)
            {
                await ClientStream.TryDisposeAsync().ConfigureAwait(false);
                ClientStream = null;
            }

            if (Connection != null)
            {
                await Connection.TryDisposeAsync().ConfigureAwait(false);
                Connection = null;
            }

            throw;
        }
    }

    /// <summary>
    /// Creates <see cref="RemotingSession"/> for the incoming QUIC connection.
    /// </summary>
    private async Task<Guid> CreateRemotingSession()
    {
        using var receiveLock = await ReceiveLock;
        var lengthBuffer = new byte[4];
        await ClientStream.ReadExactlyAsync(lengthBuffer).ConfigureAwait(false);
        var messageSize = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);

        if (messageSize < 0 || messageSize > MaxMessageSize)
            throw new InvalidOperationException($"Invalid message size: {messageSize}");

        var handshakeBytes = new byte[messageSize];
        await ClientStream.ReadExactlyAsync(handshakeBytes).ConfigureAwait(false);

        var serializer = RemotingServer.Serializer;
        var handshakeMessage = serializer.Deserialize<ClientHandshakeMessage>(handshakeBytes);
        handshakeMessage.ClientAddress = Connection.RemoteEndPoint.ToString();

        Session = await RemotingServer.SessionRepository
            .ResumeOrCreateSession(handshakeMessage, RemotingServer, this)
                .ConfigureAwait(false);

        return Session.SessionId;
    }
}
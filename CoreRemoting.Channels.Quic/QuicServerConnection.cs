using System;
using System.IO;
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
    }

    private IRemotingServer RemotingServer { get; set; }

    private RemotingSession Session { get; set; }

    /// <inheritdoc/>
    public async Task<bool> SendMessageAsync(byte[] rawMessage)
    {
        try
        {
            if (rawMessage.Length > MaxMessageSize)
                throw new InvalidOperationException("Message is too large. Max size: " +
                    MaxMessageSize + ", actual size: " + rawMessage.Length);

            using var sendLock = await SendLock;

            // message length + message body
            ClientWriter.Write7BitEncodedInt(rawMessage.Length);
            await ClientStream.WriteAsync(rawMessage, 0, rawMessage.Length);
            return true;
        }
        catch (Exception ex)
        {
            LastException = ex as NetworkException ??
                new NetworkException(ex.Message, ex);

            OnErrorOccured(ex.Message, ex);
            return false;
        }
    }

    /// <summary>
    /// Starts listening to the incoming messages.
    /// </summary>
    public async Task<Guid> StartListening()
    {
        var sessionId = await CreateRemotingSession();
        _ = Task.Run(ReadIncomingMessages);
        return sessionId;
    }

    /// <summary>
    /// Creates <see cref="RemotingSession"/> for the incoming QUIC connection.
    /// </summary>
    private async Task<Guid> CreateRemotingSession()
    {
        // read handshake message
        var clientPublicKey = await ReadIncomingMessage()
            .ConfigureAwait(false);

        // disable message encryption if handshake is empty
        if (clientPublicKey != null && clientPublicKey.Length == 0)
            clientPublicKey = null;

        Session = RemotingServer.SessionRepository.CreateSession(
            clientPublicKey, Connection.RemoteEndPoint.ToString(),
                RemotingServer, this);

        return Session.SessionId;
    }

    private async Task<byte[]> ReadIncomingMessage()
    {
    	using var receiveLock = await ReceiveLock;
        var messageSize = ClientReader.Read7BitEncodedInt();
        return ClientReader.ReadBytes(Math.Min(messageSize, MaxMessageSize));
    }

    private async Task ReadIncomingMessages()
    {
        try
        {
            while (true)
            {
                var message = await ReadIncomingMessage()
                    .ConfigureAwait(false);

                OnReceiveMessage(message ?? []);
            }
        }
        catch (Exception ex)
        {
            LastException = ex as NetworkException ??
                new NetworkException(ex.Message, ex);

            OnErrorOccured(ex.Message, LastException);
            OnDisconnected();
        }
        finally
        {
            await (Connection?.DisposeAsync() ?? default);
            Connection = null;
        }
    }
}

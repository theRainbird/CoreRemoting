using System;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Text;
using System.Threading.Tasks;

namespace CoreRemoting.Channels.Quic;

/// <summary>
/// Client side QUIC channel implementation, based on System.Net.Quic.
/// </summary>
public class QuicClientChannel : QuicTransport, IClientChannel, IRawMessageTransport
{
    /// <summary>
    /// Gets or sets the URL this channel is connected to.
    /// </summary>
    public string Url { get; private set; }

    private Uri Uri { get; set; }

    private IRemotingClient Client { get; set; }

    private QuicClientConnectionOptions Options { get; set; }

    /// <inheritdoc />
    public IRawMessageTransport RawMessageTransport => this;

    /// <inheritdoc />
    public void Init(IRemotingClient client)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        if (!QuicConnection.IsSupported)
            throw new NotSupportedException("QUIC is not supported.");

        Url = "quic://" +
            client.Config.ServerHostName + ":" +
            client.Config.ServerPort + "/rpc";

        Uri = new Uri(Url);

        // prepare QUIC client connection options
        Options = new()
        {
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, Uri.Port), //new DnsEndPoint(Uri.Host, Uri.Port),
            DefaultStreamErrorCode = 0x0A,
            DefaultCloseErrorCode = 0x0B,
            MaxInboundUnidirectionalStreams = 10,
            MaxInboundBidirectionalStreams = 100,
            ClientAuthenticationOptions = new()
            {
                // accept self-signed certificates generated on-the-fly
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true,
                ApplicationProtocols =
                [
                    new SslApplicationProtocol(ProtocolName)
                ]
            }
        };
    }

    /// <inheritdoc />
    public async Task ConnectAsync()
    {
        // connect and open duplex stream
        Connection = await QuicConnection.ConnectAsync(Options).ConfigureAwait(false);
        ClientStream = await Connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional).ConfigureAwait(false);
        ClientReader = new(ClientStream, Encoding.UTF8, leaveOpen: true);
        ClientWriter = new(ClientStream, Encoding.UTF8, leaveOpen: true);

        // prepare handshake message
        var handshakeMessage = Array.Empty<byte>();
        if (Client.MessageEncryption)
        {
            handshakeMessage = Client.PublicKey;
        }

        // start listening for incoming messages
        IsConnected = true;
        StartListening();

        // send handshake message
        await SendMessageAsync(handshakeMessage).ConfigureAwait(false);
        OnConnected();
    }

    public virtual void StartListening()
    {
        _ = Task.Run(ReadIncomingMessages);
    }

    private async Task ReadIncomingMessages()
    {
        try
        {
            while (IsConnected)
            {
                using var receiveLock = await ReceiveLock;
                var messageSize = ClientReader.Read7BitEncodedInt();
                var message = ClientReader.ReadBytes(Math.Min(messageSize, MaxMessageSize));
                OnReceiveMessage(message);
            }
        }
        catch (Exception ex)
        {
            LastException = ex as NetworkException ??
                new NetworkException(ex.Message, ex);

            OnErrorOccured(ex.Message, ex);
            OnDisconnected();
        }
        finally
        {
            await DisconnectAsync()
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
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
            await ClientStream.WriteAsync(rawMessage, 0, rawMessage.Length)
                .ConfigureAwait(false);

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

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        await Connection.CloseAsync(0x0C)
            .ConfigureAwait(false);

        IsConnected = false;
        OnDisconnected();
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (Connection == null)
            return;

        if (IsConnected)
            await DisconnectAsync()
                .ConfigureAwait(false);

        await Connection.DisposeAsync()
            .ConfigureAwait(false);
        Connection = null;

        // clean up readers/writers
        ClientReader.Dispose();
        ClientReader = null;
        ClientWriter.Dispose();
        ClientWriter = null;

        await base.DisposeAsync()
            .ConfigureAwait(false);
    }
}

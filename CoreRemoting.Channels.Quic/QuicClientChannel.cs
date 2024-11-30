using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Text;
using System.Threading.Tasks;

namespace CoreRemoting.Channels.Quic;

/// <summary>
/// Client side QUIC channel implementation based on System.Net.Quic.
/// </summary>
public class QuicClientChannel : IClientChannel, IRawMessageTransport
{
    internal const int MaxMessageSize = 1024 * 1024 * 128;
    internal const string ProtocolName = nameof(CoreRemoting);

    /// <summary>
    /// Gets or sets the URL this channel is connected to.
    /// </summary>
    public string Url { get; private set; }

    private Uri Uri { get; set; }

    private IRemotingClient Client { get; set; }

    private QuicClientConnectionOptions Options { get; set; }

    private QuicConnection Connection { get; set; }

    private QuicStream ClientStream { get; set; }

    private BinaryReader ClientReader { get; set; }

    private BinaryWriter ClientWriter { get; set; }

    /// <inheritdoc />
    public bool IsConnected { get; private set; }

    /// <inheritdoc />
    public IRawMessageTransport RawMessageTransport => this;

    /// <inheritdoc />
    public NetworkException LastException { get; set; }

    /// <summary>
    /// Event: fires when the channel is connected.
    /// </summary>
    public event Action Connected;

    /// <inheritdoc />
    public event Action Disconnected;

    /// <inheritdoc />
    public event Action<byte[]> ReceiveMessage;

    /// <inheritdoc />
    public event Action<string, Exception> ErrorOccured;

    /// <inheritdoc />
    public void Init(IRemotingClient client)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        if (!QuicConnection.IsSupported)
            throw new NotSupportedException("QUIC is not supported.");

        Url =
            "quic://" +
            client.Config.ServerHostName + ":" +
            Convert.ToString(client.Config.ServerPort) +
            "/rpc";

        Uri = new Uri(Url);

        // prepare QUIC client connection options
        Options = new QuicClientConnectionOptions
        {
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, Uri.Port), //new DnsEndPoint(Uri.Host, Uri.Port),
            DefaultStreamErrorCode = 0x0A,
            DefaultCloseErrorCode = 0x0B,
            MaxInboundUnidirectionalStreams = 10,
            MaxInboundBidirectionalStreams = 100,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions()
            {
                // accept self-signed certificates generated on-the-fly
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true,
                ApplicationProtocols = new List<SslApplicationProtocol>()
                {
                    new SslApplicationProtocol(ProtocolName)
                }
            }
        };
    }

    /// <inheritdoc />
    public void Connect()
    {
        ConnectTask = ConnectTask ?? Task.Factory.StartNew(async () =>
        {
            // connect and open duplex stream
            Connection = await QuicConnection.ConnectAsync(Options);
            ClientStream = await Connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
            ClientReader = new BinaryReader(ClientStream, Encoding.UTF8, leaveOpen: true);
            ClientWriter = new BinaryWriter(ClientStream, Encoding.UTF8, leaveOpen: true);

            // prepare handshake message
            var handshakeMessage = Array.Empty<byte>();
            if (Client.MessageEncryption)
            {
                handshakeMessage = Client.PublicKey;
            }

            // send handshake message
            SendMessage(handshakeMessage);
            IsConnected = true;
            Connected?.Invoke();

            // start listening for incoming messages
            _ = Task.Factory.StartNew(() => StartListening());
        });

        ConnectTask.ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
    }

    private Task ConnectTask { get; set; }

    private void StartListening()
    {
        try
        {
            while (IsConnected)
            {
                var messageSize = ClientReader.Read7BitEncodedInt();
                var message = ClientReader.ReadBytes(Math.Min(messageSize, MaxMessageSize));
                if (message.Length > 0)
                {
                    ReceiveMessage(message);
                }
            }
        }
        catch (Exception ex)
        {
            LastException = ex as NetworkException ??
                new NetworkException(ex.Message, ex);

            ErrorOccured?.Invoke(ex.Message, ex);
            Disconnected?.Invoke();
        }
        finally
        {
            Disconnect();
        }
    }

    /// <inheritdoc />
    public bool SendMessage(byte[] rawMessage)
    {
        try
        {
            if (rawMessage.Length > MaxMessageSize)
                throw new InvalidOperationException("Message is too large. Max size: " +
                    MaxMessageSize + ", actual size: " + rawMessage.Length);

            // message length + message body
            ClientWriter.Write7BitEncodedInt(rawMessage.Length);
            ClientWriter.Write(rawMessage, 0, rawMessage.Length);
            return true;
        }
        catch (Exception ex)
        {
            LastException = ex as NetworkException ??
                new NetworkException(ex.Message, ex);

            ErrorOccured?.Invoke(ex.Message, ex);
            return false;
        }
    }

    private Task DisconnectTask { get; set; }

    /// <inheritdoc />
    public void Disconnect()
    {
        DisconnectTask = DisconnectTask ?? Task.Factory.StartNew(async () =>
        {
            await Connection.CloseAsync(0x0C);
            IsConnected = false;
            Disconnected?.Invoke();
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Connection == null)
            return;

        if (IsConnected)
            Disconnect();

        var task = DisconnectTask;
        if (task != null)
            task.ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

        Connection.DisposeAsync()
            .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        Connection = null;

        // clean up readers/writers
        ClientReader.Dispose();
        ClientReader = null;
        ClientWriter.Dispose();
        ClientWriter = null;
    }
}

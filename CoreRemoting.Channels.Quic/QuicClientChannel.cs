using System;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
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

    /// <inheritdoc />
    public IRawMessageTransport RawMessageTransport => this;

    /// <inheritdoc />
    public void Init(IRemotingClient client)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        if (!QuicConnection.IsSupported)
            throw new NotSupportedException("QUIC is not supported.");
    }

    /// <inheritdoc />
    public async Task ConnectAsync()
    {
        LastException = null;

        Url = "quic://" +
            Client.Config.ServerHostName + ":" +
            Client.Config.ServerPort + "/rpc";

        Uri = new Uri(Url);

        var options = new QuicClientConnectionOptions
        {
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, Uri.Port),
            DefaultStreamErrorCode = 0x0A,
            DefaultCloseErrorCode = 0x0B,
            MaxInboundUnidirectionalStreams = 10,
            MaxInboundBidirectionalStreams = 100,
            ClientAuthenticationOptions = new()
            {
                TargetHost = Uri.Host,
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true,
                ApplicationProtocols =
                [
                    new SslApplicationProtocol(ProtocolName)
                ]
            }
        };

        Connection = await QuicConnection.ConnectAsync(options).ConfigureAwait(false);
        ClientStream = await Connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional).ConfigureAwait(false);

        try
        {
            IsConnected = true;
            await StartListening().ConfigureAwait(false);

            var handshakeMessage = Client.HandshakeMessage;
            var handshakeBytes = Client.Serializer.Serialize(handshakeMessage);

            await SendMessageAsync(handshakeBytes).ConfigureAwait(false);

            OnConnected();
        }
        catch
        {
            IsConnected = false;
            throw;
        }
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);

        if (Connection != null)
        {
            await Connection.DisposeAsync().ConfigureAwait(false);
            Connection = null;
        }

        if (ClientStream != null)
        {
            await ClientStream.DisposeAsync().ConfigureAwait(false);
            ClientStream = null;
        }
    }
}
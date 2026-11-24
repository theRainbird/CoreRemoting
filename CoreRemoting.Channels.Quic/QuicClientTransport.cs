using System;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Threading.Tasks;
using CoreRemoting.Toolbox;

namespace CoreRemoting.Channels.Quic;

/// <summary>
/// QUIC client transport, based on System.Net.Quic. Owns the connection,
/// the bidirectional client stream and the connect mechanics.
/// </summary>
public class QuicClientTransport : QuicTransport
{
    /// <summary>
    /// The remoting client this transport belongs to.
    /// </summary>
    private IRemotingClient Client { get; }

    /// <summary>
    /// Gets the URL of the remote endpoint.
    /// </summary>
    public string Url { get; private set; }

    /// <summary>
    /// The parsed URI of the remote endpoint.
    /// </summary>
    private Uri Uri { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="QuicClientTransport"/> class.
    /// </summary>
    /// <param name="client">The remoting client</param>
    /// <exception cref="NotSupportedException">Thrown when QUIC is not supported on this platform.</exception>
    public QuicClientTransport(IRemotingClient client)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));

        if (!QuicConnection.IsSupported)
            throw new NotSupportedException("QUIC is not supported.");
    }

    /// <summary>
    /// Establishes the QUIC connection and opens the bidirectional client stream.
    /// </summary>
    public async Task OpenAsync()
    {
        LastException = null;

        Url = "quic://" + Client.Config.ServerHostName + ":" + Client.Config.ServerPort + "/rpc";
        Uri = new Uri(Url);

        var options = new QuicClientConnectionOptions
        {
            // TODO: resolve the host name instead of using the loopback address
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, Uri.Port),
            DefaultStreamErrorCode = 0x0A,
            DefaultCloseErrorCode = 0x0B,
            MaxInboundUnidirectionalStreams = 10,
            MaxInboundBidirectionalStreams = 100,
            ClientAuthenticationOptions = new()
            {
                TargetHost = Uri.Host,
                RemoteCertificateValidationCallback =
                    (sender, certificate, chain, errors) => true,
                ApplicationProtocols =
                [
                    new SslApplicationProtocol(ProtocolName)
                ]
            }
        };

        Connection = await QuicConnection
            .ConnectAsync(options)
            .ConfigureAwait(false);

        ClientStream = await Connection
            .OpenOutboundStreamAsync(QuicStreamType.Bidirectional)
            .ConfigureAwait(false);

        IsConnectedFlag = true;
    }

    /// <summary>
    /// Resets the connection state after a failed connect attempt, so the
    /// transport is not reported as connected when a post-connect step failed.
    /// </summary>
    public void MarkFailed() =>
        IsConnectedFlag = false;

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync()
            .ConfigureAwait(false);

        await Connection.TryDisposeAsync()
            .ConfigureAwait(false);
        Connection = null;

        await ClientStream.TryDisposeAsync()
            .ConfigureAwait(false);
        ClientStream = null;
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Threading.Tasks;

namespace CoreRemoting.Channels.Quic;

/// <summary>
/// Server side QUIC channel implementation based on System.Net.Quic.
/// </summary>
public class QuicServerChannel : IServerChannel
{
    private IRemotingServer Server { get; set; }

    private ConcurrentDictionary<Guid, QuicServerConnection> Connections { get; } =
        new ConcurrentDictionary<Guid, QuicServerConnection>();

    /// <inheritdoc/>
    public bool IsListening { get; private set; }

    private QuicServerConnectionOptions Options { get; set; }

    private QuicListener Listener { get; set; }

    private IPEndPoint ListenEndPoint { get; set; }

    /// <inheritdoc/>
    public void Init(IRemotingServer server)
    {
        Server = server ?? throw new ArgumentNullException(nameof(server));
        if (!QuicListener.IsSupported)
            throw new NotSupportedException("QUIC is not supported.");

        var url = "quic://" +
            Server.Config.HostName + ":" +
            Server.Config.NetworkPort + "/rpc";

        // validate URL and create listener endpoint
        var uri = new Uri(url);
        var certificate = CertificateHelper.GenerateSelfSigned(uri.DnsSafeHost);
        ListenEndPoint = new IPEndPoint(IPAddress.Loopback, uri.Port); // TODO: Loopback

        Options = new QuicServerConnectionOptions()
        {
            DefaultStreamErrorCode = 0x0A,
            DefaultCloseErrorCode = 0x0B,
            ServerAuthenticationOptions = new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                ApplicationProtocols = new List<SslApplicationProtocol>()
                {
                    new SslApplicationProtocol(QuicClientChannel.ProtocolName)
                },
            }
        };
    }

    /// <inheritdoc/>
    public void StartListening()
    {
        _ = Task.Factory.StartNew(async () =>
        {
            // start the listener
            Listener = await QuicListener.ListenAsync(new QuicListenerOptions()
            {
                ListenEndPoint = ListenEndPoint,
                ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(Options),
                ApplicationProtocols = new List<SslApplicationProtocol>()
                {
                    new SslApplicationProtocol(QuicClientChannel.ProtocolName)
                },
            });

            // accept incoming connections
            IsListening = true;
            while (IsListening)
            {
                try
                {
                    var connection = await Listener.AcceptConnectionAsync();
                    var stream = await connection.AcceptInboundStreamAsync();
                    var session = new QuicServerConnection(connection, stream, Server);
                    var sessionId = session.StartListening();
                    Connections[sessionId] = session;
                }
                catch
                {
                    IsListening = false; // TODO: not sure??
                }
            }
        });
    }

    /// <inheritdoc/>
    public void StopListening()
    {
        if (Listener != null && IsListening)
        {
            IsListening = false;
            Listener.DisposeAsync()
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        StopListening();
        Listener = null;
    }
}
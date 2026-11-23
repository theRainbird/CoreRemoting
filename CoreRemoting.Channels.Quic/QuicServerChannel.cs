using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Threading.Tasks;
using CoreRemoting.Channels.Common;
using CoreRemoting.Toolbox;

namespace CoreRemoting.Channels.Quic;

/// <summary>
/// Server side QUIC channel implementation, based on System.Net.Quic.
/// </summary>
public class QuicServerChannel : ServerChannelBase
{
    private ConcurrentDictionary<Guid, QuicServerConnection> Connections { get; } = new();

    private bool _isListening;

    /// <inheritdoc/>
    public override bool IsListening => _isListening;

    private QuicServerConnectionOptions Options { get; set; }

    private QuicListener Listener { get; set; }

    private IPEndPoint ListenEndPoint { get; set; }

    /// <inheritdoc/>
    public override void Init(IRemotingServer server)
    {
        base.Init(server);

        if (!QuicListener.IsSupported)
            throw new NotSupportedException("QUIC is not supported.");

        var url = "quic://" +
            Server.Config.HostName + ":" +
            Server.Config.NetworkPort + "/rpc";

        // validate URL and create listener endpoint
        var uri = new Uri(url);
        var certificate = CertificateHelper.GenerateSelfSigned(uri.DnsSafeHost);
        ListenEndPoint = new IPEndPoint(IPAddress.Loopback, uri.Port); // TODO: Loopback

        Options = new()
        {
            DefaultStreamErrorCode = 0x0A,
            DefaultCloseErrorCode = 0x0B,
            ServerAuthenticationOptions = new()
            {
                ServerCertificate = certificate,
                ApplicationProtocols =
                [
                    new SslApplicationProtocol(QuicTransport.ProtocolName)
                ],
            }
        };
    }

    /// <inheritdoc/>
    public override void StartListening()
    {
        _ = Task.Run(async () =>
        {
            // start the listener
            Listener = await QuicListener.ListenAsync(new()
            {
                ListenEndPoint = ListenEndPoint,
                ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(Options),
                ApplicationProtocols =
                [
                    new SslApplicationProtocol(QuicTransport.ProtocolName)
                ],
            })
            .ConfigureAwait(false);

            // accept incoming connections
            _isListening = true;
            while (IsListening)
            {
                await ReceiveConnection()
                    .ConfigureAwait(false);
            }
        });
    }

    private async Task ReceiveConnection()
    {
        try
        {
            var connection = await Listener.AcceptConnectionAsync()
                .ConfigureAwait(false);

            var stream = await connection.AcceptInboundStreamAsync()
                .ConfigureAwait(false);

            var session = new QuicServerConnection(connection, stream, Server);
            var sessionId = await session.StartListening()
                .ConfigureAwait(false);

            Connections[sessionId] = session;
            session.Disconnected += () =>
                // the session should have been already disposed of here
                Connections.TryRemove(sessionId, out _);
        }
        catch
        {
            // a single failed connection must not stop the accept loop
        }
    }

    /// <inheritdoc/>
    public override void StopListening() =>
        StopListeningAsync().JustWait();

    private async Task StopListeningAsync()
    {
        if (Listener != null && IsListening)
        {
            _isListening = false;
            await Listener.DisposeAsync()
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await StopListeningAsync()
            .ConfigureAwait(false);

        Listener = null;

        var activeConnections = Connections.Values.ToArray();
        Connections.Clear();

        await DisposeConnectionsAsync(activeConnections)
            .ConfigureAwait(false);
    }
}
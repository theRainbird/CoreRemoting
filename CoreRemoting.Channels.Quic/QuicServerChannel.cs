using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Threading.Tasks;
using CoreRemoting.Toolbox;

namespace CoreRemoting.Channels.Quic;

/// <summary>
/// Server side QUIC channel implementation, based on System.Net.Quic.
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
    public void StartListening()
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
            IsListening = true;
            while (IsListening)
                await ReceiveConnection();
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
            session.Disconnected += async () =>
            {
                Connections.TryRemove(sessionId, out session);
                await session.DisposeAsync();
            };
        }
        catch
        {
            IsListening = false; // TODO: not sure??
        }
    }

    /// <inheritdoc/>
    public void StopListening() =>
        StopListeningAsync().JustWait();

    private async Task StopListeningAsync()
    {
        if (Listener != null && IsListening)
        {
            IsListening = false;
            await Listener.DisposeAsync()
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopListeningAsync()
            .ConfigureAwait(false);

        Listener = null;

        foreach (var conn in Connections.Values.ToArray())
            await conn.DisposeAsync()
                .ConfigureAwait(false);

        Connections.Clear();
    }
}
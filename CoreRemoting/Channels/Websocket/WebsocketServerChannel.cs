using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using CoreRemoting.Channels.Common;
using CoreRemoting.Toolbox;

namespace CoreRemoting.Channels.Websocket;

/// <summary>
/// Server side websocket channel implementation based on System.Net.Websockets and HttpListener.
/// </summary>
public class WebsocketServerChannel : ServerChannelBase
{
    private HttpListener HttpListener { get; set; }

    private ConcurrentDictionary<Guid, WebsocketServerConnection> Connections { get; } = new();

    /// <inheritdoc/>
    public override bool IsListening => HttpListener.IsListening;

    /// <inheritdoc/>
    public override void Init(IRemotingServer server)
    {
        if (!HttpListener.IsSupported)
            throw new NotSupportedException("HttpListener not supported by this platform.");

        base.Init(server);

        HttpListener = new();

        var prefix = "http://" +
            Server.Config.HostName + ":" +
            Server.Config.NetworkPort + "/";

        HttpListener.Prefixes.Add(prefix);
    }

    /// <inheritdoc/>
    public override void StartListening()
    {
        HttpListener.Start();

        _ = Task.Run(async () =>
        {
            while (IsListening)
            {
                try
                {
                    await ReceiveConnection()
                        .ConfigureAwait(false);
                }
                catch
                {
                    // a single failed connection
                    // must not stop the accept loop
                }
            }
        });
    }

    private async Task ReceiveConnection()
    {
        // check if it's a websocket request
        var context = await HttpListener.GetContextAsync()
            .ConfigureAwait(false);

        if (!context.Request.IsWebSocketRequest)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.Close();
            return;
        }

        // accept websocket request and start a new session
        var websocketContext = await context.AcceptWebSocketAsync(null)
            .ConfigureAwait(false);

        var websocket = websocketContext.WebSocket;
        var connection = new WebsocketServerConnection(
            context.Request.RemoteEndPoint.ToString(),
            websocketContext, websocket, Server);

        // handle incoming websocket messages
        var sessionId = await connection.StartListening()
            .ConfigureAwait(false);

        RegisterConnection(Connections, sessionId, connection);
    }

    /// <inheritdoc/>
    public override void StopListening()
    {
        if (HttpListener != null && IsListening)
            HttpListener.Stop();
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        StopListening();
        HttpListener = null;

        var connections = Connections.Values.ToArray();
        Connections.Clear();

        await DisposeConnectionsAsync(connections)
            .ConfigureAwait(false);
    }
}

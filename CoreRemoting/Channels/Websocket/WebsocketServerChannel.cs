using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Tasks;

namespace CoreRemoting.Channels.Websocket;

/// <summary>
/// Server side websocket channel implementation based on System.Net.Websockets and HttpListener.
/// </summary>
public class WebsocketServerChannel : IServerChannel
{
    private HttpListener HttpListener { get; set; }

    private IRemotingServer Server { get; set; }

    private ConcurrentDictionary<Guid, WebsocketServerConnection> Connections { get; } = new();

    /// <inheritdoc/>
    public bool IsListening => HttpListener.IsListening;

    /// <inheritdoc/>
    public void Init(IRemotingServer server)
    {
        if (!HttpListener.IsSupported)
            throw new NotSupportedException("HttpListener not supported by this platform.");

        Server = server ?? throw new ArgumentNullException(nameof(server));
        HttpListener = new();

        var prefix = "http://" +
            Server.Config.HostName + ":" +
            Server.Config.NetworkPort + "/";

        HttpListener.Prefixes.Add(prefix);
    }

    /// <inheritdoc/>
    public void StartListening()
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

        Connections[sessionId] = connection;

        // don't await DisposeAsync from the loop's finally, that would deadlock;
        // dispose off the loop's thread, after it has fully completed
        connection.Disconnected += () =>
        {
            if (Connections.TryRemove(sessionId, out var conn))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await conn.DisposeAsync()
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // async void event handler should never throw
                    }
                });
            }
        };
    }

    /// <inheritdoc/>
    public void StopListening()
    {
        if (HttpListener != null && IsListening)
            HttpListener.Stop();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        StopListening();
        HttpListener = null;

        foreach (var conn in Connections.Values)
            await conn.DisposeAsync()
                .ConfigureAwait(false);

        Connections.Clear();
    }
}

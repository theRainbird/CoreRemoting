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

    private ConcurrentDictionary<Guid, WebsocketServerConnection> Connections { get; } =
        new ConcurrentDictionary<Guid, WebsocketServerConnection>();

    /// <inheritdoc/>
    public bool IsListening => HttpListener.IsListening;

    /// <inheritdoc/>
    public void Init(IRemotingServer server)
    {
        Server = server ?? throw new ArgumentNullException(nameof(server));
        HttpListener = new HttpListener();

        var prefix = "http://" +
            Server.Config.HostName + ":" +
            Server.Config.NetworkPort + "/";
        HttpListener.Prefixes.Add(prefix);
    }

    /// <inheritdoc/>
    public void StartListening()
    {
        _ = Task.Factory.StartNew(async () =>
        {
            HttpListener.Start();
            while (IsListening)
                await ReceiveConnection();
        });
    }

    private async Task ReceiveConnection()
    {
        // check if it's a websocket request
        var context = await HttpListener.GetContextAsync();
        if (!context.Request.IsWebSocketRequest)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.Close();
            return;
        }

        // accept websocket request and start a new session
        var websocketContext = await context.AcceptWebSocketAsync(null);
        var websocket = websocketContext.WebSocket;
        var connection = new WebsocketServerConnection(
            context.Request.RemoteEndPoint.ToString(),
            websocketContext, websocket, Server);

        // handle incoming websocket messages
        var sessionId = connection.StartListening();
        Connections[sessionId] = connection;
    }

    /// <inheritdoc/>
    public void StopListening()
    {
        if (HttpListener != null && IsListening)
            HttpListener.Stop();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        StopListening();
        HttpListener = null;
    }
}
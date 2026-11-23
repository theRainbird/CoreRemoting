using System;
using System.Threading.Tasks;
using CoreRemoting.Channels.Common;
using WebSocketSharp.Server;

namespace CoreRemoting.Channels.WebsocketSharp;

/// <summary>
/// Server side websocket channel implementation based on websocket-sharp.
/// </summary>
public class WebsocketSharpServerChannel : ServerChannelBase
{
    private WebSocketServer _webSocketServer;

    /// <summary>
    /// Initializes the channel.
    /// </summary>
    /// <param name="server">CoreRemoting sever</param>
    public override void Init(IRemotingServer server)
    {
        base.Init(server);

        _webSocketServer = new WebSocketServer(Server.Config.NetworkPort, secure: false)
        {
            NoDelay = true
        };

        _webSocketServer.AddWebSocketService(
            path: "/rpc",
            initializer: () => new RpcWebsocketSharpBehavior(Server));
    }

    /// <summary>
    /// Start listening for client requests.
    /// </summary>
    public override void StartListening()
    {
        if (_webSocketServer == null)
            throw new InvalidOperationException("Channel is not initialized.");

        _webSocketServer.Start();
    }

    /// <summary>
    /// Stop listening for client requests.
    /// </summary>
    public override void StopListening()
    {
        if (_webSocketServer == null)
            throw new InvalidOperationException("Channel is not initialized.");

        if (_webSocketServer.IsListening)
            _webSocketServer.Stop();
    }

    /// <summary>
    /// Gets whether the channel is listening or not.
    /// </summary>
    public override bool IsListening => _webSocketServer != null && _webSocketServer.IsListening;

    /// <summary>
    /// Stops listening and frees managed resources.
    /// </summary>
    public override ValueTask DisposeAsync()
    {
        StopListening();
        _webSocketServer = null;
        Server = null;
        return default;
    }
}

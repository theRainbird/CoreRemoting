using System;
using WebSocketSharp.Server;

namespace CoreRemoting.Channels.Websocket
{
    /// <summary>
    /// Server side websocket channel implementation.
    /// </summary>
    public class WebsocketServerChannel : IServerChannel
    {
        private WebSocketServer _webSocketServer;
        private IRemotingServer _server;

        /// <summary>
        /// Initializes the channel.
        /// </summary>
        /// <param name="server">CoreRemoting sever</param>
        public void Init(IRemotingServer server)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            
            _webSocketServer = new WebSocketServer(_server.Config.NetworkPort, secure: false);
            
            _webSocketServer.AddWebSocketService(
                path: "/rpc",
                initializer: () => new RpcWebsocketSharpBehavior(_server));
        }

        /// <summary>
        /// Start listening for client requests.
        /// </summary>
        public void StartListening()
        {
            if (_webSocketServer == null)
                throw new InvalidOperationException("Channel is not initialized.");
            
            _webSocketServer.Start();
        }

        /// <summary>
        /// Stop listening for client requests.
        /// </summary>
        public void StopListening()
        {
            if (_webSocketServer == null)
                throw new InvalidOperationException("Channel is not initialized.");
            
            if (_webSocketServer.IsListening)
                _webSocketServer.Stop();
        }

        /// <summary>
        /// Gets whether the channel is listening or not.
        /// </summary>
        public bool IsListening => _webSocketServer != null && _webSocketServer.IsListening;

        /// <summary>
        /// Stops listening and frees managed resources.
        /// </summary>
        public void Dispose()
        {
            StopListening();
            _webSocketServer = null;
            _server = null;
        }
    }
}
using System;
using System.Security.Cryptography.X509Certificates;
using WebSocketSharp.Server;

namespace CoreRemoting.Channels.Websocket
{
    /// <summary>
    /// Executes RPC calls from clients.
    /// </summary>
    public class WebsocketServerChannel : IServerChannel
    {
        private WebSocketServer _webSocketServer;
        private IRemotingServer _server;

        public void Init(IRemotingServer server)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            
            _webSocketServer = new WebSocketServer(_server.Config.NetworkPort, secure: false);
            
            _webSocketServer.AddWebSocketService(
                path: "/rpc",
                initializer: () => new RpcWebsocketSharpBehavior(_server));
        }

        public void StartListening()
        {
            if (_webSocketServer == null)
                throw new InvalidOperationException("Channel is not initialized.");
            
            _webSocketServer.Start();
        }

        public void StopListening()
        {
            if (_webSocketServer == null)
                throw new InvalidOperationException("Channel is not initialized.");
            
            if (_webSocketServer.IsListening)
                _webSocketServer.Stop();
        }

        public bool IsListening => _webSocketServer != null && _webSocketServer.IsListening;

        /// <summary>
        /// Frees managed ressources.
        /// </summary>
        public void Dispose()
        {
            StopListening();
            _webSocketServer = null;
            _server = null;
        }
    }
}
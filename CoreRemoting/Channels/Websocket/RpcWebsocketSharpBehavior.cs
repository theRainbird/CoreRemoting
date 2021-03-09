using System;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace CoreRemoting.Channels.Websocket
{
    /// <summary>
    /// Executes RPC calls from clients.
    /// </summary>
    public class RpcWebsocketSharpBehavior : WebSocketBehavior, IRawMessageTransport, IDisposable
    {
        private IRemotingServer _server;
        private RemotingSession _session;
        
        /// <summary>
        /// Event: Fired when a message is received via websocket.
        /// </summary>
        public event Action<byte[]> ReceiveMessage;
        
        /// <summary>
        /// Event: Fires when an error is occurred.
        /// </summary>
        public event Action<string, Exception> ErrorOccured;
        
        /// <summary>
        /// Initializes the RPC service instance.
        /// </summary>
        /// <param name="server">Remoting server instance, which is hosting the service to call</param>
        public RpcWebsocketSharpBehavior(IRemotingServer server)
        {
            _server = server ?? throw new ArgumentException(nameof(server));
        }

        /// <summary>
        /// Sends a message over the websocket.
        /// </summary>
        /// <param name="rawMessage">Raw data of the message</param>
        public void SendMessage(byte[] rawMessage)
        {
            Send(rawMessage);
        }
        
        /// <summary>
        /// Called when a message from a client is received.
        /// </summary>
        /// <param name="e">Metadata and the message from client</param>
        protected override void OnMessage(MessageEventArgs e)
        {
            if (_session == null)
            {
                byte[] clientPublicKey = null;
            
                var messageEncryptionCookie = Context.CookieCollection["MessageEncryption"];

                if (messageEncryptionCookie?.Value == "1")
                {
                    var shakeHandsCookie = Context.CookieCollection["ShakeHands"];

                    clientPublicKey =
                        Convert.FromBase64String(
                            shakeHandsCookie.Value);
                }
            
                _session = 
                    _server.SessionRepository.CreateSession(
                        clientPublicKey,
                        _server,
                        this);
                
                _session.BeforeDispose += BeforeDisposeSession;
            }
            else
                ReceiveMessage?.Invoke(e.RawData);
        }

        /// <summary>
        /// Closes the internal websocket session.
        /// </summary>
        private void BeforeDisposeSession()
        {
            _session = null;
            Sessions.CloseSession(ID);
        }

        /// <summary>
        /// Event procedure: Called, if an error occurs at the websocket layer.
        /// </summary>
        /// <param name="e">Message and optional Exception info</param>
        protected override void OnError(ErrorEventArgs e)
        {
            LastException = new NetworkException(e.Message, e.Exception);
            
            ErrorOccured?.Invoke(e.Message, e.Exception);
        }

        /// <summary>
        /// Frees managed resources.
        /// </summary>
        public void Dispose()
        {
            _server = null;
        }

        /// <summary>
        /// Gets or sets the last exception.
        /// </summary>
        public NetworkException LastException { get; set; }
    }
}
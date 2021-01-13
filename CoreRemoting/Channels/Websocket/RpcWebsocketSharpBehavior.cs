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
        
        public event Action<byte[]> ReceiveMessage;
        
        public event Action<string, Exception> ErrorOccured;
        
        /// <summary>
        /// Initializes the RPC service instance.
        /// </summary>
        /// <param name="server">Remoting server instance, which is hosting the service to call</param>
        public RpcWebsocketSharpBehavior(IRemotingServer server)
        {
            _server = server ?? throw new ArgumentException(nameof(server));
        }

        public void SendMessage(byte[] rawMessage)
        {
            Send(rawMessage);
        }

        /// <summary>
        /// Performs a security handshake between client and server in order to establish a session.
        /// </summary>
        protected override void OnOpen()
        {
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

        private void BeforeDisposeSession()
        {
            Guid sessionId = _session.SessionId;
            _session = null;
            Sessions.CloseSession(ID);
        }

        protected override void OnError(ErrorEventArgs e)
        {
            LastException = new NetworkException(e.Message, e.Exception);
            
            ErrorOccured?.Invoke(e.Message, e.Exception);
        }

        /// <summary>
        /// Frees managed ressources.
        /// </summary>
        public void Dispose()
        {
            _server = null;
        }

        public NetworkException LastException { get; set; }
    }
}
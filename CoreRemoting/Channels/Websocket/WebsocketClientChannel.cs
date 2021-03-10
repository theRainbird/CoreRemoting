using System;
using WebSocketSharp;
using WebSocketSharp.Net;

namespace CoreRemoting.Channels.Websocket
{
    /// <summary>
    /// Client side websocket channel implementation.
    /// </summary>
    public class WebsocketClientChannel : IClientChannel, IRawMessageTransport
    {
        private WebSocket _webSocket;
        
        /// <summary>
        /// Event: Fires when a message is received from server.
        /// </summary>
        public event Action<byte[]> ReceiveMessage;
        
        /// <summary>
        /// Event: Fires when an error is occurred.
        /// </summary>
        public event Action<string, Exception> ErrorOccured;
        
        /// <summary>
        /// Initializes the channel.
        /// </summary>
        /// <param name="client">CoreRemoting client</param>
        public void Init(IRemotingClient client)
        {
            string url = 
                "ws://" + 
                client.Config.ServerHostName + ":" + 
                Convert.ToString(client.Config.ServerPort) + 
                "/rpc";
            
            _webSocket = new WebSocket(url);

            _webSocket.SetCookie(new Cookie(
                name: "MessageEncryption",
                value: client.MessageEncryption ? "1" : "0"));

            if (client.MessageEncryption)
            {
                _webSocket.SetCookie(new Cookie(
                    "ShakeHands",
                    Convert.ToBase64String(client.PublicKey)));
            }

            _webSocket.Log.Output = (timestamp, text) => Console.WriteLine("{0}: {1}", timestamp, text);
            _webSocket.Log.Level = LogLevel.Debug;
        }

        /// <summary>
        /// Establish a websocket connection with the server.
        /// </summary>
        public void Connect()
        {
            if (_webSocket == null)
                throw new InvalidOperationException("Channel is not initialized.");
            
            if (_webSocket.IsAlive)
                return;

            LastException = null;
            
            _webSocket.OnMessage += OnMessage;
            _webSocket.OnError += OnError;

            _webSocket.Connect();
            _webSocket.Send(string.Empty);
        }

        /// <summary>
        /// Event procedure: Called when a error occurs on the websocket layer.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnError(object sender, ErrorEventArgs e)
        {
            LastException = new NetworkException(e.Message, e.Exception);
            
            ErrorOccured?.Invoke(e.Message, e.Exception);
        }

        /// <summary>
        /// Closes the websocket connection.
        /// </summary>
        public void Disconnect()
        {
            if (_webSocket == null)
                return;
            
            _webSocket.OnMessage -= OnMessage;
            _webSocket.OnError -= OnError;
            
            _webSocket.Close(CloseStatusCode.Normal);
        }

        /// <summary>
        /// Gets whether the websocket connection is established or not.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                if (_webSocket == null)
                    throw new InvalidOperationException("Channel is not initialized.");

                return _webSocket.IsAlive;
            }
        }

        /// <summary>
        /// Gets the raw message transport component for this connection.
        /// </summary>
        public IRawMessageTransport RawMessageTransport => this;
        
        /// <summary>
        /// Disconnect and free manages resources.
        /// </summary>
        public void Dispose()
        {
            Disconnect();
            _webSocket = null;
        }

        /// <summary>
        /// Event procedure: Called when a message from server is received.
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments containing the message content</param>
        private void OnMessage(object sender, MessageEventArgs e)
        {
            ReceiveMessage?.Invoke(e.RawData);
        }
        
        /// <summary>
        /// Sends a message to the server.
        /// </summary>
        /// <param name="rawMessage">Raw message data</param>
        public void SendMessage(byte[] rawMessage)
        {
            _webSocket.Send(rawMessage);
        }

        /// <summary>
        /// Gets or sets the last exception.
        /// </summary>
        public NetworkException LastException { get; set; }
    }
}
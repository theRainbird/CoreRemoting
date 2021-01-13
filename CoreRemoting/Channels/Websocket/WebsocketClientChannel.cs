using System;
using System.Security.Authentication;
using WebSocketSharp;
using WebSocketSharp.Net;

namespace CoreRemoting.Channels.Websocket
{
    public class WebsocketClientChannel : IClientChannel, IRawMessageTransport
    {
        private WebSocket _webSocket;
        
        public event Action<byte[]> ReceiveMessage;
        
        public event Action<string, Exception> ErrorOccured;
        
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

        private void OnError(object sender, ErrorEventArgs e)
        {
            LastException = new NetworkException(e.Message, e.Exception);
            
            ErrorOccured?.Invoke(e.Message, e.Exception);
        }

        public void Disconnect()
        {
            if (_webSocket == null)
                return;
            
            _webSocket.OnMessage -= OnMessage;
            _webSocket.OnError -= OnError;
            
            _webSocket.Close(CloseStatusCode.Normal);
        }

        public bool IsConnected
        {
            get
            {
                if (_webSocket == null)
                    throw new InvalidOperationException("Channel is not initialized.");

                return _webSocket.IsAlive;
            }
        }

        public IRawMessageTransport RawMessageTransport => this;
        
        public void Dispose()
        {
            Disconnect();
            _webSocket = null;
        }

        private void OnMessage(object sender, MessageEventArgs e)
        {
            ReceiveMessage?.Invoke(e.RawData);
        }
        
        public void SendMessage(byte[] rawMessage)
        {
            _webSocket.Send(rawMessage);
        }

        public NetworkException LastException { get; set; }
    }
}
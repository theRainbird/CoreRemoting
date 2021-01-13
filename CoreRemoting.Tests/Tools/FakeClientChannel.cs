using System;
using CoreRemoting.Channels;

namespace CoreRemoting.Tests.Tools
{
    public class FakeClientChannel : IClientChannel, IRawMessageTransport
    {
        private bool _connected = false;
        
        public void Init(IRemotingClient client)
        {   
        }

        private void FakeTransportOnClientMessageReceived(byte[] rawMessage)
        {
            ReceiveMessage?.Invoke(rawMessage);
        }

        public void Connect()
        {
            if (_connected)
                return;

            FakeTransport.ClientMessageReceived += FakeTransportOnClientMessageReceived;
            _connected = true;
            
            FakeTransport.SendMessageToServer(new byte[0]);
        }

        public void Disconnect()
        {
            if (!_connected)
                return;
            
            FakeTransport.ClientMessageReceived -= FakeTransportOnClientMessageReceived;
            _connected = false;
        }

        public bool IsConnected => _connected;
        public IRawMessageTransport RawMessageTransport => this;
        
        public event Action<byte[]> ReceiveMessage;
        
        public event Action<string, Exception> ErrorOccured;
        public NetworkException LastException { get; set; }
        public void SendMessage(byte[] rawMessage)
        {
            FakeTransport.SendMessageToServer(rawMessage);
        }
        
        public void Dispose()
        {
            Disconnect();
        }
    }
}
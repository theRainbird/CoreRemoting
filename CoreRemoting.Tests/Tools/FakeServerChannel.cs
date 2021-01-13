using System;
using CoreRemoting.Channels;

namespace CoreRemoting.Tests.Tools
{
    public class FakeServerChannel : IServerChannel, IRawMessageTransport
    {
        private bool _listening = false;
        private IRemotingServer _server;
        
        public void Init(IRemotingServer server)
        {
            _server = server;
        }

        public void StartListening()
        {
            if (_listening)
                return;

            FakeTransport.ServerMessageReceived += FakeTransportOnServerMessageReceived;
            _listening = true;
        }

        private void FakeTransportOnServerMessageReceived(byte[] rawMessage)
        {
            if (rawMessage.Length == 0)
            {
                _server.SessionRepository.CreateSession(
                    null,
                    _server,
                    this);
            }
            else
                ReceiveMessage?.Invoke(rawMessage);
        }

        public void StopListening()
        {
            if (!_listening)
                return;

            FakeTransport.ServerMessageReceived -= FakeTransportOnServerMessageReceived;
            _listening = false;
        }

        public bool IsListening => _listening;
        public event Action<byte[]> ReceiveMessage;
        public event Action<string, Exception> ErrorOccured;
        public NetworkException LastException { get; set; }
        public void SendMessage(byte[] rawMessage)
        {
            FakeTransport.SendMessageToClient(rawMessage);
        }
        
        public void Dispose()
        {
            StopListening();
            _server = null;
        }
    }
}
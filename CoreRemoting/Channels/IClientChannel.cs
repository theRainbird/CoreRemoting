using System;

namespace CoreRemoting.Channels
{
    public interface IClientChannel : IDisposable
    {
        void Init(IRemotingClient client);
        
        void Connect();

        void Disconnect();
        
        bool IsConnected { get; }
        
        IRawMessageTransport RawMessageTransport { get; }
    }
}
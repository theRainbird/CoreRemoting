using System;

namespace CoreRemoting.Channels
{
    public interface IServerChannel : IDisposable
    {
        void Init(IRemotingServer server);
        
        void StartListening();
        void StopListening();
        
        bool IsListening { get; }
    }
}
using System;
using System.Collections.Generic;
using CoreRemoting.Channels;

namespace CoreRemoting
{
    public interface ISessionRepository : IDisposable
    {
        int KeySize { get; }

        RemotingSession CreateSession(
            byte[] clientPublicKey, 
            IRemotingServer server,
            IRawMessageTransport rawMessageTransport);
        RemotingSession GetSession(Guid sessionId);
        
        IEnumerable<RemotingSession> Sessions { get; }
        
        void RemoveSession(Guid sessionId);
    }
}
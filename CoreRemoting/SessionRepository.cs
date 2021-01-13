using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using CoreRemoting.Channels;

namespace CoreRemoting
{
    public class SessionRepository : ISessionRepository
    {
        private readonly ConcurrentDictionary<Guid, RemotingSession> _sessions;

        public SessionRepository(int keySize)
        {
            KeySize = keySize;
            _sessions = new ConcurrentDictionary<Guid, RemotingSession>();
        }

        public int KeySize { get; }

        public RemotingSession CreateSession(byte[] clientPublicKey, IRemotingServer server, IRawMessageTransport rawMessageTransport)
        {
            if (server == null)
                throw new ArgumentException(nameof(server));
            
            if (rawMessageTransport == null)
                throw new ArgumentNullException(nameof(rawMessageTransport));
            
            var session = new RemotingSession(
                KeySize, 
                clientPublicKey,
                server,
                rawMessageTransport);
            
            _sessions.TryAdd(session.SessionId, session);
            
            return session;
        }

        public RemotingSession GetSession(Guid sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
                return session;
            
            throw new KeyNotFoundException($"Session '{sessionId}' not found.");
        }

        public void RemoveSession(Guid sessionId)
        {
            if (_sessions.TryRemove(sessionId, out var session))
                session.Dispose();
        }

        public IEnumerable<RemotingSession> Sessions => _sessions.Values.ToArray();

        public void Dispose()
        {
            while (_sessions.Count > 0)
            {
                var sessionId = _sessions.First().Key;
                RemoveSession(sessionId);
            }
        }
    }
}
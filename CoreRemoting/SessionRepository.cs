using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using CoreRemoting.Channels;

namespace CoreRemoting
{
    /// <summary>
    /// Default in-memory session repository.
    /// </summary>
    public class SessionRepository : ISessionRepository
    {
        private readonly ConcurrentDictionary<Guid, RemotingSession> _sessions;

        /// <summary>
        /// Creates a new instance of the SessionRepository class.
        /// </summary>
        /// <param name="keySize">Key size for asymmetric encryption. Should be 3072 or better in 2021 (Please use steps os 1024).</param>
        public SessionRepository(int keySize)
        {
            KeySize = keySize;
            _sessions = new ConcurrentDictionary<Guid, RemotingSession>();
        }

        /// <summary>
        /// Gets the key size for asymmetric encryption. Should be 3072 or better in 2021 ;)
        /// </summary>
        public int KeySize { get; }

        /// <summary>
        /// Creates a new session.
        /// </summary>
        /// <param name="clientPublicKey">Client's public key</param>
        /// <param name="server">Server instance</param>
        /// <param name="rawMessageTransport">Component that does the raw message transport</param>
        /// <returns>The newly created session</returns>
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

        /// <summary>
        /// Gets a specified session by its ID.
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <returns>The session correlating to the specified session ID</returns>
        /// <exception cref="KeyNotFoundException">Thrown, if no session with the secified session ID is found</exception>
        public RemotingSession GetSession(Guid sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
                return session;
            
            throw new KeyNotFoundException($"Session '{sessionId}' not found.");
        }

        /// <summary>
        /// Removes a specified seesion by its ID.
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        public void RemoveSession(Guid sessionId)
        {
            if (_sessions.TryRemove(sessionId, out var session))
                session.Dispose();
        }

        /// <summary>
        /// Gets a list of all sessions.
        /// </summary>
        public IEnumerable<RemotingSession> Sessions => _sessions.Values.ToArray();

        /// <summary>
        /// Frees managed resources.
        /// </summary>
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
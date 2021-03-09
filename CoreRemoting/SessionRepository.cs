using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using CoreRemoting.Channels;

namespace CoreRemoting
{
    /// <summary>
    /// Default in-memory session repository.
    /// </summary>
    public class SessionRepository : ISessionRepository
    {
        private readonly ConcurrentDictionary<Guid, RemotingSession> _sessions;
        private Timer _inactiveSessionSweepTimer;
        private readonly int _maximumSessionInactivityTime;

        /// <summary>
        /// Creates a new instance of the SessionRepository class.
        /// </summary>
        /// <param name="keySize">Key size for asymmetric encryption. Should be 3072 or better in 2021 (Please use steps of 1024).</param>
        /// <param name="inactiveSessionSweepInterval">Sweep interval for inactive sessions in seconds (No session sweeping, if set to 0)</param>
        /// <param name="maximumSessionInactivityTime">Maximum session inactivity time in minutes</param>
        public SessionRepository(int keySize, int inactiveSessionSweepInterval, int maximumSessionInactivityTime)
        {
            KeySize = keySize;
            _sessions = new ConcurrentDictionary<Guid, RemotingSession>();

            _maximumSessionInactivityTime = maximumSessionInactivityTime;

            StartInactiveSessionSweepTimer(inactiveSessionSweepInterval);
        }

        /// <summary>
        /// Starts the inactive session sweep timer.
        /// </summary>
        /// <param name="inactiveSessionSweepInterval">Sweep interval for inactive sessions in seconds</param>
        private void StartInactiveSessionSweepTimer(int inactiveSessionSweepInterval)
        {
            if (inactiveSessionSweepInterval <= 0)
                return;
            
            _inactiveSessionSweepTimer =
                new Timer(Convert.ToDouble(inactiveSessionSweepInterval * 1000));

            _inactiveSessionSweepTimer.Elapsed += InactiveSessionSweepTimerOnElapsed;
            _inactiveSessionSweepTimer.Start();
        }

        /// <summary>
        /// Event procedure: Called when the inactive session sweep timer elapses. 
        /// </summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event arguments</param>
        private void InactiveSessionSweepTimerOnElapsed(object sender, ElapsedEventArgs e)
        {
            if (_inactiveSessionSweepTimer == null)
                return;
            
            if (!_inactiveSessionSweepTimer.Enabled)
                return;
            
            var inactiveSessionIdList =
                _sessions
                    .Where(item => 
                        DateTime.Now.Subtract(item.Value.LastActivityTimestamp).TotalMinutes > _maximumSessionInactivityTime)
                    .Select(item => item.Key);

            foreach (var inactiveSessionId in inactiveSessionIdList)
            {
                RemoveSession(inactiveSessionId);
            }
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
        /// <exception cref="KeyNotFoundException">Thrown, if no session with the specified session ID is found</exception>
        public RemotingSession GetSession(Guid sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
                return session;
            
            throw new KeyNotFoundException($"Session '{sessionId}' not found.");
        }

        /// <summary>
        /// Removes a specified session by its ID.
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
            if (_inactiveSessionSweepTimer != null)
            {
                _inactiveSessionSweepTimer.Stop();
                _inactiveSessionSweepTimer.Dispose();
                _inactiveSessionSweepTimer = null;
            }

            while (_sessions.Count > 0)
            {
                var sessionId = _sessions.First().Key;
                RemoveSession(sessionId);
            }
        }
    }
}
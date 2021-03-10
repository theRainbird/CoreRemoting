using System;
using System.Collections.Generic;
using CoreRemoting.Channels;

namespace CoreRemoting
{
    /// <summary>
    /// Interface to be implemented by CoreRemoting session repository classes.
    /// </summary>
    public interface ISessionRepository : IDisposable
    {
        /// <summary>
        /// Gets the key size for asymmetric encryption. Should be 3072 or better in 2021 ;)
        /// </summary>
        int KeySize { get; }

        /// <summary>
        /// Creates a new session.
        /// </summary>
        /// <param name="clientPublicKey">Client's public key</param>
        /// <param name="server">Server instance</param>
        /// <param name="rawMessageTransport">Component that does the raw message transport</param>
        /// <returns>The newly created session</returns>
        RemotingSession CreateSession(
            byte[] clientPublicKey, 
            IRemotingServer server,
            IRawMessageTransport rawMessageTransport);
        
        /// <summary>
        /// Gets a specified session by its ID.
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <returns>The session correlating to the specified session ID</returns>
        RemotingSession GetSession(Guid sessionId);
        
        /// <summary>
        /// Gets a list of all sessions.
        /// </summary>
        IEnumerable<RemotingSession> Sessions { get; }
        
        /// <summary>
        /// Removes a specified session by its ID.
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        void RemoveSession(Guid sessionId);
    }
}
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CoreRemoting.Channels;

namespace CoreRemoting;

/// <summary>
/// Interface to be implemented by CoreRemoting session repository classes.
/// </summary>
public interface ISessionRepository : IAsyncDisposable
{
    /// <summary>
    /// Creates a new session.
    /// </summary>
    /// <param name="messageEncryption">Whether message encryption is enabled on client</param>
    /// <param name="clientPublicKey">Client's public key</param>
    /// <param name="clientAddress">Client's network address</param>
    /// <param name="server">Server instance</param>
    /// <param name="rawMessageTransport">Component that does the raw message transport</param>
    /// <returns>The newly created session</returns>
    Task<RemotingSession> CreateSession(
        bool messageEncryption,
        byte[] clientPublicKey,
        string clientAddress,
        IRemotingServer server,
        IRawMessageTransport rawMessageTransport);

    /// <summary>
    /// Tries to resume a specified session on the given raw message transport.
    /// The presented public key has to match the public key of the original connection (hijack protection).
    /// </summary>
    /// <param name="sessionId">Session ID of the session to be resumed</param>
    /// <param name="clientPublicKey">Client's public key, as presented by the reconnecting client</param>
    /// <param name="rawMessageTransport">Component that does the raw message transport of the reconnected client</param>
    /// <returns>The resumed session, or null if the session doesn't exist or can't be resumed (callers then have to fall back to creating a new session)</returns>
    Task<RemotingSession> TryResumeSession(
        Guid sessionId,
        byte[] clientPublicKey,
        IRawMessageTransport rawMessageTransport);

    /// <summary>
    /// Gets a list of all sessions.
    /// </summary>
    IEnumerable<RemotingSession> Sessions { get; }

    /// <summary>
    /// Removes a specified session by its ID.
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    Task RemoveSession(Guid sessionId);
}
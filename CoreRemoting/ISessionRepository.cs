using System;
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
    /// <param name="clientPublicKey">Client's public key</param>
    /// <param name="clientAddress">Client's network address</param>
    /// <param name="server">Server instance</param>
    /// <param name="rawMessageTransport">Component that does the raw message transport</param>
    /// <returns>The newly created session</returns>
    Task<RemotingSession> CreateSession(
        byte[] clientPublicKey,
        string clientAddress,
        IRemotingServer server,
        IRawMessageTransport rawMessageTransport);

    /// <summary>
    /// Removes a specified session by its ID.
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    Task RemoveSession(Guid sessionId);
}
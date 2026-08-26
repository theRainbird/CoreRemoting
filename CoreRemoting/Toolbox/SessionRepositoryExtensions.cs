using System;
using System.Threading.Tasks;
using CoreRemoting.Channels;

namespace CoreRemoting.Toolbox;

/// <summary>
/// Extension methods for <see cref="ISessionRepository"/>.
/// </summary>
public static class SessionRepositoryExtensions
{
    /// <summary>
    /// Attempts to resume an existing session, or creates a new one if resumption isn't possible.
    /// <para>
    /// Resumption is skipped when <paramref name="sessionId"/> is null or <see cref="Guid.Empty"/>.
    /// If <see cref="ISessionRepository.TryResumeSession"/> returns null (session not found,
    /// public key mismatch, or session can't be resumed), a new session is created as a fallback.
    /// </para>
    /// </summary>
    /// <param name="repository">Session repository.</param>
    /// <param name="sessionId">Session ID to resume, or null / <see cref="Guid.Empty"/> to skip resumption.</param>
    /// <param name="messageEncryption">Whether message encryption is enabled on client.</param>
    /// <param name="clientPublicKey">Client's public key (validated on resume, stored on create).</param>
    /// <param name="clientAddress">Client's network address (used only when creating a new session).</param>
    /// <param name="server">Server instance (used only when creating a new session).</param>
    /// <param name="rawMessageTransport">Raw message transport of the current connection.</param>
    /// <returns>The resumed session, or a newly created one if resumption failed.</returns>
    public static async Task<RemotingSession> ResumeOrCreateSession(
        this ISessionRepository repository,
        Guid? sessionId,
        bool messageEncryption,
        byte[] clientPublicKey,
        string clientAddress,
        IRemotingServer server,
        IRawMessageTransport rawMessageTransport)
    {
        if (repository == null)
            throw new ArgumentNullException(nameof(repository));

        if (sessionId.HasValue && sessionId.Value != Guid.Empty)
        {
            var resumed = await repository
                .TryResumeSession(sessionId.Value, clientPublicKey, rawMessageTransport)
                .ConfigureAwait(false);

            if (resumed != null)
                return resumed;
        }

        return await repository
            .CreateSession(messageEncryption, clientPublicKey, clientAddress, server, rawMessageTransport)
            .ConfigureAwait(false);
    }
}

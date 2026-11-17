using System;
using System.Threading.Tasks;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Manages server-side event subscriptions for remote sessions.
/// </summary>
internal interface IServerSubscriptionService
{
    /// <summary>
    /// Removes all handlers registered by the specified session.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>Number of removed occurrences.</returns>
    Task<int> RemoveAllForSession(Guid sessionId);

    /// <summary>
    /// Removes handlers with the given keys across all registered services.
    /// Idempotent: unknown keys are silently ignored.
    /// </summary>
    /// <param name="handlerKeys">Handler keys to remove.</param>
    /// <returns>Number of removed occurrences.</returns>
    Task<int> RemoveHandlers(Guid[] handlerKeys);
}
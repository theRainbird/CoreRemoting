using System;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Server-side service for managing subscriptions across all event hubs.
/// </summary>
internal interface IServerSubscriptionService
{
    /// <summary>
    /// Removes all subscriptions registered by the specified session.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>Number of removed subscriptions.</returns>
    int RemoveAllForSession(Guid sessionId);
}
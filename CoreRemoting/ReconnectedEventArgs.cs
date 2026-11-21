using System;

namespace CoreRemoting;

/// <summary>
/// Event arguments for the <see cref="RemotingClient.Reconnected"/> event.
/// </summary>
public sealed class ReconnectedEventArgs : EventArgs
{
    /// <summary>
    /// Creates a new instance of the ReconnectedEventArgs class.
    /// </summary>
    /// <param name="sessionResumed">Whether the previous session was resumed (true) or a brand-new session was created (false).</param>
    /// <param name="sessionId">The ID of the session that is active after the reconnect.</param>
    /// <param name="subscriptionsRestored">Whether event subscriptions were successfully re-established on the new connection.</param>
    public ReconnectedEventArgs(bool sessionResumed, Guid sessionId, bool subscriptionsRestored)
    {
        SessionResumed = sessionResumed;
        SessionId = sessionId;
        SubscriptionsRestored = subscriptionsRestored;
    }

    /// <summary>
    /// Gets whether the previous session was resumed (true) or a brand-new session
    /// was created because the old one could not be resumed (false).
    /// </summary>
    public bool SessionResumed { get; }

    /// <summary>
    /// Gets the ID of the session that is active after the reconnect.
    /// </summary>
    public Guid SessionId { get; }

    /// <summary>
    /// Gets whether the client's event subscriptions were successfully re-established
    /// on the new connection (verified against the server's subscription checksum).
    /// </summary>
    public bool SubscriptionsRestored { get; }
}

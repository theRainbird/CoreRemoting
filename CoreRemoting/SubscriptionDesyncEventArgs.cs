using System;

namespace CoreRemoting;

/// <summary>
/// Event arguments for the <see cref="RemotingClient.SubscriptionsDesyncDetected"/> event.
/// </summary>
public sealed class SubscriptionDesyncEventArgs : EventArgs
{
    /// <summary>
    /// Creates a new instance of the SubscriptionDesyncEventArgs class.
    /// </summary>
    /// <param name="clientChecksum">Subscription checksum as computed on the client side.</param>
    /// <param name="serverChecksum">Subscription checksum as reported by the server (null, if the server could not be reached).</param>
    public SubscriptionDesyncEventArgs(string clientChecksum, string serverChecksum)
    {
        ClientChecksum = clientChecksum;
        ServerChecksum = serverChecksum;
    }

    /// <summary>
    /// Gets the client-side subscription checksum.
    /// </summary>
    public string ClientChecksum { get; }

    /// <summary>
    /// Gets the server-side subscription checksum (null, if the server could not be reached).
    /// </summary>
    public string ServerChecksum { get; }
}

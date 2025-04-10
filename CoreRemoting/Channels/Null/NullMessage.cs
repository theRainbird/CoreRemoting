namespace CoreRemoting.Channels.Null;

/// <summary>
/// Channel message.
/// </summary>
/// <param name="sender">Sender endpoint</param>
/// <param name="receiver">Receiver endpoint</param>
/// <param name="message">Message payload</param>
/// <param name="metadata">Message metadata</param>
public class NullMessage(string sender, string receiver, byte[] message, params string[] metadata)
{
    /// <summary>
    /// Gets message sender.
    /// </summary>
    public string Sender { get; } = sender;

    /// <summary>
    /// Gets message receiver.
    /// </summary>
    public string Receiver { get; } = receiver;

    /// <summary>
    /// Gets message payload.
    /// </summary>
    public byte[] Message { get; } = message;

    /// <summary>
    /// Gets the metadata.
    /// </summary>
    public string[] Metadata { get; } = metadata;
}
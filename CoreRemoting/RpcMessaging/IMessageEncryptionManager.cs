namespace CoreRemoting.RpcMessaging
{
    /// <summary>
    /// Interface of message encryption manager component.
    /// </summary>
    public interface IMessageEncryptionManager
    {
        /// <summary>
        /// Creates a new CoreRemoting generic wire message.
        /// </summary>
        /// <param name="messageType">Message type (defines the type of content)</param>
        /// <param name="serializedMessage">Raw data of the serialized message</param>
        /// <param name="sharedSecret">Optional shared secret, if message should be encrypted</param>
        /// <param name="error">Sets whether this message defines an error state</param>
        /// <param name="uniqueCallKey">Unique key to correlate the RPC call</param>
        /// <returns>Wire message object</returns>
        WireMessage CreateWireMessage(
            string messageType,
            byte[] serializedMessage,
            byte[] sharedSecret = null,
            bool error = false,
            byte[] uniqueCallKey = null);

        /// <summary>
        /// Gets the decrypted message data of a specified wire message.
        /// </summary>
        /// <param name="message">Wire message to decrypt</param>
        /// <param name="sharedSecret">Shared secret to be used for decryption</param>
        /// <returns>Decrypted content of the wire message</returns>
        byte[] GetDecryptedMessageData(
            WireMessage message,
            byte[] sharedSecret = null);
    }
}
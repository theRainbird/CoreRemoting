using System;
using CoreRemoting.Encryption;
using CoreRemoting.Serialization;

namespace CoreRemoting.RpcMessaging
{
    /// <summary>
    /// Interface of message encryption manager component.
    /// </summary>
    public interface IMessageEncryptionManager
    {
        /// <summary>
        /// Creates a new wire message.
        /// </summary>
        /// <param name="messageType">Message type name</param>
        /// <param name="serializedMessage">Serialized message</param>
        /// <param name="serializer">Serializer used to serialize the signed content</param>
        /// <param name="keyPair">RSA key pair to be used for creating a RSA signature for the message data</param>
        /// <param name="sharedSecret">Shared secret (wire message will be not encrypted, if null)</param>
        /// <param name="error">Species whether the wire message is in error state</param>
        /// <param name="uniqueCallKey">Unique key to correlate RPC call</param>
        /// <returns>The created wire message</returns>
        /// <exception cref="ArgumentException">Thrown if the message type is left empty.</exception>
        public WireMessage CreateWireMessage(
            string messageType,
            byte[] serializedMessage,
            ISerializerAdapter serializer,
            RsaKeyPair keyPair = null,
            byte[] sharedSecret = null,
            bool error = false,
            byte[] uniqueCallKey = null);

        /// <summary>
        /// Gets decrypted data from a wire message.
        /// </summary>
        /// <param name="message">Wire message</param>
        /// <param name="serializer">Serializer used to deserialized the signed content</param>
        /// <param name="sharedSecret">Shared secret (null, if the wire message is not encrypted)</param>
        /// <param name="sendersPublicKeyBlob">Public key of the sender used for RSA signature verification</param>
        /// <param name="sendersPublicKeySize">Sender's public key size</param>
        /// <returns>Decrypted raw data</returns>
        public byte[] GetDecryptedMessageData(
            WireMessage message,
            ISerializerAdapter serializer,
            byte[] sharedSecret = null,
            byte[] sendersPublicKeyBlob = null,
            int sendersPublicKeySize = 0);
    }
}
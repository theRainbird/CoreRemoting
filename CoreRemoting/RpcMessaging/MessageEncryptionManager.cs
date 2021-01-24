using System;
using CoreRemoting.Encryption;

namespace CoreRemoting.RpcMessaging
{
    /// <summary>
    /// Provides tools for wire message creation an encryption.
    /// </summary>
    public class MessageEncryptionManager : IMessageEncryptionManager
    {
        /// <summary>
        /// Creates a new wire message.
        /// </summary>
        /// <param name="messageType">Message type name</param>
        /// <param name="serializedMessage">Serialized message</param>
        /// <param name="sharedSecret">Shared secret (wire message will be not encrypted, if null)</param>
        /// <param name="error">Species whether the wire message is in error state</param>
        /// <param name="uniqueCallKey">Unique key to correlate RPC call</param>
        /// <returns>The created wire message</returns>
        /// <exception cref="ArgumentException">Thrown if the message type is left empty.</exception>
        public WireMessage CreateWireMessage(
            string messageType,
            byte[] serializedMessage,
            byte[] sharedSecret = null,
            bool error = false,
            byte[] uniqueCallKey = null)
        {
            if (string.IsNullOrWhiteSpace(messageType))
                throw new ArgumentException("Message type must not be empty.", nameof(messageType));
            
            byte[] iv = 
                sharedSecret == null
                    ? new byte[0]
                    : AesEncryption.GenerateIv();

            byte[] messageContent =
                sharedSecret == null
                    ? serializedMessage
                    : AesEncryption.Encrypt(
                        dataToEncrypt: serializedMessage,
                        sharedSecret: sharedSecret,
                        iv: iv);
            
            return 
                new WireMessage()
                {
                    MessageType = messageType,
                    Data = messageContent,
                    Iv = iv,
                    Error = error,
                    UniqueCallKey = uniqueCallKey
                };
        }

        /// <summary>
        /// Gets decrpyted data from a wire message.
        /// </summary>
        /// <param name="message">Wire message</param>
        /// <param name="sharedSecret">Shared secret (null, if the wire message is not encrypted)</param>
        /// <returns>Decrpyted raw data</returns>
        public byte[] GetDecryptedMessageData(
            WireMessage message, 
            byte[] sharedSecret = null)
        {
            if (message.Iv.Length > 0 && sharedSecret != null)
            {
                return
                    AesEncryption.Decrypt(
                        encryptedData: message.Data,
                        sharedSecret: sharedSecret,
                        iv: message.Iv);
            }
            else
                return message.Data;
        }
    }
}
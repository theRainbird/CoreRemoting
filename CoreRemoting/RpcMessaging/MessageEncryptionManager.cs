using System;
using System.Security;
using CoreRemoting.Encryption;
using CoreRemoting.Serialization;

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
            byte[] uniqueCallKey = null)
        {
            if (string.IsNullOrWhiteSpace(messageType))
                throw new ArgumentException("Message type must not be empty.", nameof(messageType));

            byte[] iv = 
                sharedSecret == null
                    ? Array.Empty<byte>()
                    : AesEncryption.GenerateIv();
            
            byte[] rawContent;
            
            if (keyPair != null && sharedSecret != null)
            {
                var signedMessageData =
                    new SignedMessageData()
                    {
                        MessageRawData = serializedMessage,
                        Signature =
                            RsaSignature.CreateSignature(
                                keySize: keyPair.KeySize,
                                sendersPrivateKeyBlob: keyPair.PrivateKey,
                                rawData: serializedMessage)
                    };

                rawContent = serializer.Serialize(typeof(SignedMessageData), signedMessageData);
            }
            else
            {
                rawContent = serializedMessage;
            }
            
            byte[] messageContent =
                sharedSecret == null
                    ? rawContent
                    : AesEncryption.Encrypt(
                        dataToEncrypt: rawContent,
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
            int sendersPublicKeySize = 0)
        {
            if (message.Iv.Length > 0 && sharedSecret != null)
            {
                var decryptedRawData =
                    AesEncryption.Decrypt(
                        encryptedData: message.Data,
                        sharedSecret: sharedSecret,
                        iv: message.Iv);

                var signedMessageData = serializer.Deserialize<SignedMessageData>(decryptedRawData);
                
                if (sendersPublicKeyBlob != null && signedMessageData.Signature != null)
                {
                    if (RsaSignature.VerifySignature(
                        keySize: sendersPublicKeySize,
                        sendersPublicKeyBlob: sendersPublicKeyBlob,
                        rawData: signedMessageData.MessageRawData,
                        signature: signedMessageData.Signature))
                        return signedMessageData.MessageRawData;
                    else
                        throw new SecurityException("Verification of message signature failed.");
                }
                else
                    return decryptedRawData;
            }
            else
                return message.Data;
        }
    }
}
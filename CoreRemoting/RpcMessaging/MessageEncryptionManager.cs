using System;
using System.Security;
using CoreRemoting.Encryption;
using CoreRemoting.Serialization;

namespace CoreRemoting.RpcMessaging;

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
    /// <param name="keyPair">Session key pair to be used for creating a signature for the message data</param>
    /// <param name="sharedSecret">Shared secret (wire message will be not encrypted, if null)</param>
    /// <param name="error">Species whether the wire message is in error state</param>
    /// <param name="uniqueCallKey">Unique key to correlate RPC call</param>
    /// <returns>The created wire message</returns>
    /// <exception cref="ArgumentException">Thrown if the message type is left empty.</exception>
    public WireMessage CreateWireMessage(
        string messageType,
        byte[] serializedMessage,
        ISerializerAdapter serializer,
        ISessionKeyPair keyPair = null,
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
                new SignedMessageData
                {
                    MessageRawData = serializedMessage,
                    Signature = keyPair.CreateSignature(serializedMessage)
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
            new WireMessage
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
    /// <returns>Decrypted raw data</returns>
    public byte[] GetDecryptedMessageData(
        WireMessage message,
        ISerializerAdapter serializer,
        byte[] sharedSecret = null,
        byte[] sendersPublicKeyBlob = null)
    {
        if (message.Iv.Length > 0 && sharedSecret != null)
        {
            var decryptedRawData =
                AesEncryption.Decrypt(
                    encryptedData: message.Data,
                    sharedSecret: sharedSecret,
                    iv: message.Iv);

            if (sendersPublicKeyBlob != null)
            {
                var signedMessageData =
                    serializer.Deserialize<SignedMessageData>(decryptedRawData);

                if (signedMessageData.Signature != null)
                {
                    using var verifier = SessionKeyPairFactory.FromPublicKey(sendersPublicKeyBlob);

                    return verifier.VerifySignature(
                        data: signedMessageData.MessageRawData,
                        signature: signedMessageData.Signature)
                            ? signedMessageData.MessageRawData
                            : throw new SecurityException("Verification of message signature failed.");
                }
            }

            return decryptedRawData;
        }

        return message.Data;
    }
}
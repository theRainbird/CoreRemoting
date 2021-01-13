using System;
using System.Security.Cryptography;
using CoreRemoting.Encryption;

namespace CoreRemoting.RpcMessaging
{
    public class MessageEncryptionManager : IMessageEncryptionManager
    {
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
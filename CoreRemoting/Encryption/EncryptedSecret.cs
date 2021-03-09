using System;

namespace CoreRemoting.Encryption
{
    /// <summary>
    /// Describes an encrypted secret.
    /// </summary>
    [Serializable]
    public class EncryptedSecret
    {
        /// <summary>
        /// Creates a new instance of the EncryptedSecret class.
        /// </summary>
        /// <param name="encryptedSessionKey">Encrypted session key</param>
        /// <param name="iv">Initialization vector</param>
        /// <param name="encryptedMessage">Encrypted message</param>
        /// <param name="sendersPublicKeyBlob">Public key of the sender</param>
        public EncryptedSecret(byte[] encryptedSessionKey, byte[] iv, byte[] encryptedMessage, byte[] sendersPublicKeyBlob)
        {
            Iv = iv;
            EncryptedMessage = encryptedMessage;
            EncryptedSessionKey = encryptedSessionKey;
            SendersPublicKeyBlob = sendersPublicKeyBlob;
        }
        
        /// <summary>
        /// Gets the encrypted session key.
        /// </summary>
        public byte[] EncryptedSessionKey { get; private set; }
        
        /// <summary>
        /// Gets the encrypted message.
        /// </summary>
        public byte[] EncryptedMessage { get; private set; }
        
        /// <summary>
        /// Gets the initialization vector.
        /// </summary>
        public byte[] Iv { get; private set; }
        
        /// <summary>
        /// Gets the public key of the sender.
        /// </summary>
        public byte[] SendersPublicKeyBlob { get; private set; }
    }
}
using System;

namespace CoreRemoting.Encryption
{
    /// <summary>
    /// Describes an ecrypted secret.
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
        public EncryptedSecret(byte[] encryptedSessionKey, byte[] iv, byte[] encryptedMessage)
        {
            Iv = iv;
            EncryptedMessage = encryptedMessage;
            EncryptedSessionKey = encryptedSessionKey;
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
    }
}
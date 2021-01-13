using System;

namespace CoreRemoting.Encryption
{
    [Serializable]
    public class EncryptedSecret
    {
        public EncryptedSecret(byte[] encryptedSessionKey, byte[] iv, byte[] encryptedMessage)
        {
            Iv = iv;
            EncryptedMessage = encryptedMessage;
            EncryptedSessionKey = encryptedSessionKey;
        }
        
        public byte[] EncryptedSessionKey { get; private set; }
        public byte[] EncryptedMessage { get; private set; }
        public byte[] Iv { get; private set; }
    }
}
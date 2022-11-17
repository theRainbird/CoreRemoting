using System.Security.Cryptography;
using System.IO;

namespace CoreRemoting.Encryption
{
    /// <summary>
    /// Provides methods to perform a RSA key exchange.
    /// </summary>
    public static class RsaKeyExchange
    {
        /// <summary>
        /// Encrypts a secret with asymmetric RSA algorithm.
        /// </summary>
        /// <param name="keySize">Key size (1024, 2048, 4096, ...)</param>
        /// <param name="receiversPublicKeyBlob">Public key of the receiver</param>
        /// <param name="secretToEncrypt">Secret to encrypt</param>
        /// <param name="sendersPublicKeyBlob">Public key of the sender (It's not needed to encrypt the secret, but to transfer the sender's public key to the receiver)</param>
        /// <returns>Encrypted secret</returns>
        public static EncryptedSecret EncryptSecret(int keySize, byte[] receiversPublicKeyBlob, byte[] secretToEncrypt, byte[] sendersPublicKeyBlob)
        {
            using var receiversPublicKey = new RSACryptoServiceProvider(dwKeySize: keySize);
            receiversPublicKey.ImportCspBlob(receiversPublicKeyBlob);
            
            using Aes aes = new AesCryptoServiceProvider();

            // Encrypt the session key
            var keyFormatter = new RSAPKCS1KeyExchangeFormatter(receiversPublicKey);
            
            // Encrypt the secret
            using MemoryStream ciphertext = new MemoryStream();
            using CryptoStream stream = new CryptoStream(ciphertext, aes.CreateEncryptor(), CryptoStreamMode.Write);
            
            stream.Write(secretToEncrypt, 0, secretToEncrypt.Length);
            stream.FlushFinalBlock();
            stream.Close();
            
            return new EncryptedSecret(
                encryptedSessionKey: keyFormatter.CreateKeyExchange(aes.Key, typeof(Aes)),
                iv: aes.IV, 
                encryptedMessage: ciphertext.ToArray(),
                sendersPublicKeyBlob: sendersPublicKeyBlob);
        }

        /// <summary>
        /// Decrypts a secret with asymmetric RSA algorithm.
        /// </summary>
        /// <param name="keySize">Key size (1024, 2048, 4096, ...)</param>
        /// <param name="receiversPrivateKeyBlob">Private key of the receiver</param>
        /// <param name="encryptedSecret">Encrypted secret</param>
        /// <returns>Decrypted secret</returns>
        public static byte[] DecryptSecret(int keySize, byte[] receiversPrivateKeyBlob, EncryptedSecret encryptedSecret)
        {
            using var receiversPrivateKey = new RSACryptoServiceProvider(dwKeySize: keySize);
            receiversPrivateKey.ImportCspBlob(receiversPrivateKeyBlob);

            using Aes aes = new AesCryptoServiceProvider();
            aes.IV = encryptedSecret.Iv;

            // Decrypt the session key
            RSAPKCS1KeyExchangeDeformatter keyDeformatter = new RSAPKCS1KeyExchangeDeformatter(receiversPrivateKey);
            aes.Key = keyDeformatter.DecryptKeyExchange(encryptedSecret.EncryptedSessionKey);

            // Decrypt the message
            using MemoryStream stream = new MemoryStream();
            using CryptoStream cryptoStream = new CryptoStream(stream, aes.CreateDecryptor(), CryptoStreamMode.Write);
            cryptoStream.Write(encryptedSecret.EncryptedMessage, 0, encryptedSecret.EncryptedMessage.Length);
            cryptoStream.FlushFinalBlock();
            cryptoStream.Close();

            return stream.ToArray();
        }
    }
}
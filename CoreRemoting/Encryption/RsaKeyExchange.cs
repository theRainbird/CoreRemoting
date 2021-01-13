using System.Security.Cryptography;
using System.IO;

namespace CoreRemoting.Encryption
{
    public static class RsaKeyExchange
    {
        public static EncryptedSecret EncryptSecret(int keySize, byte[] receiversPublicKeyBlob, byte[] secretToEncrypt)
        {
            using var receiversPublicKey = new RSACryptoServiceProvider(dwKeySize: keySize);
            receiversPublicKey.ImportCspBlob(receiversPublicKeyBlob);
            
            using Aes aes = new AesCryptoServiceProvider();

            // Encrypt the session key
            var keyFormatter = new RSAPKCS1KeyExchangeFormatter(receiversPublicKey);
            
            // Encrypt the seceret
            using MemoryStream ciphertext = new MemoryStream();
            using CryptoStream stream = new CryptoStream(ciphertext, aes.CreateEncryptor(), CryptoStreamMode.Write);
            
            stream.Write(secretToEncrypt, 0, secretToEncrypt.Length);
            stream.Close();
            
            return new EncryptedSecret(
                encryptedSessionKey: keyFormatter.CreateKeyExchange(aes.Key, typeof(Aes)),
                iv: aes.IV, 
                encryptedMessage: ciphertext.ToArray());
        }

        public static byte[] DecrpytSecret(int keySize, byte[] receiversPrivateKeyBlob, EncryptedSecret encryptedSecret)
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
            cryptoStream.Close();

            return stream.ToArray();
        }
    }
}
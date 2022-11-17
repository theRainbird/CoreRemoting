using System;
using System.IO;
using System.Security.Cryptography;

namespace CoreRemoting.Encryption
{
    /// <summary>
    /// Provides methods to implement symmetric AES encryption.
    /// </summary>
    public static class AesEncryption
    {
        /// <summary>
        /// Creates a SHA-256 hash of a specified value.
        /// </summary>
        /// <param name="value">Value to be hashed</param>
        /// <returns>SHA-256 hash</returns>
        public static byte[] CreateHash(byte[] value)
        {
            return SHA256.Create().ComputeHash(value);
        }

        /// <summary>
        /// Generates an initialization vector.
        /// </summary>
        /// <returns>Initialization vector</returns>
        /// <exception cref="NotSupportedException">Thrown if AES is not supported by the current system environment</exception>
        public static byte[] GenerateIv()
        {
            using var aes = Aes.Create();
            
            if (aes == null)
                throw new NotSupportedException();

            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();
            
            return aes.IV;
        }

        /// <summary>
        /// Encrypts raw data with AES.
        /// </summary>
        /// <param name="dataToEncrypt">Raw data to encrypt</param>
        /// <param name="sharedSecret">Shared secret</param>
        /// <param name="iv">Initialization vector</param>
        /// <returns>Encrypted data</returns>
        /// <exception cref="NotSupportedException">Thrown if AES is not supported by the current system environment</exception>
        public static byte[] Encrypt(byte[] dataToEncrypt, byte[] sharedSecret, byte[] iv)
        {
            using var aes = Aes.Create();
            
            if (aes == null)
                throw new NotSupportedException();
            
            aes.Key = CreateHash(sharedSecret);
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
  
            var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

            using var memoryStream = new MemoryStream();
            using var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write);
            
            cryptoStream.Write(dataToEncrypt, 0, dataToEncrypt.Length);
            cryptoStream.FlushFinalBlock();
            
            var encryptedData = memoryStream.ToArray();

            cryptoStream.Close();
            memoryStream.Close();
            
            return encryptedData;
        }

        /// <summary>
        /// Decrypts raw data with AES.
        /// </summary>
        /// <param name="encryptedData">Encrypted raw data</param>
        /// <param name="sharedSecret">Shared secret</param>
        /// <param name="iv">Initialization vector</param>
        /// <returns>Decrypted raw data</returns>
        /// <exception cref="NotSupportedException">Thrown if AES is not supported by the current system environment</exception>
        public static byte[] Decrypt(byte[] encryptedData, byte[] sharedSecret, byte[] iv)
        {
            using Aes aes = Aes.Create();
            
            if (aes == null)
                throw new NotSupportedException();
            
            aes.Key = CreateHash(sharedSecret);
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            
            var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

            using var memoryStream = new MemoryStream(encryptedData);
            using var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);
            using var decryptedStream = new MemoryStream();
            
            cryptoStream.CopyTo(decryptedStream);
            byte[] decryptedBytes = decryptedStream.ToArray();

            //cryptoStream.Read(decryptedBytes, 0, decryptedBytes.Length);
            
            cryptoStream.Close();
            memoryStream.Close();
            decryptedStream.Close();
            
            return decryptedBytes;
        }
    }
}
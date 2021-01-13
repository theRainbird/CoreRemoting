using System;
using System.IO;
using System.Security.Cryptography;

namespace CoreRemoting.Encryption
{
    public static class AesEncryption
    {
        public static byte[] CreateHash(byte[] value)
        {
            return SHA256.Create().ComputeHash(value);
        }

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

            using MemoryStream memoryStream = new MemoryStream(encryptedData);
            using CryptoStream cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);
            
            byte[] decryptedBytes = new byte[encryptedData.Length];
            cryptoStream.Read(decryptedBytes, 0, decryptedBytes.Length);
            
            cryptoStream.Close();
            memoryStream.Close();
            
            return decryptedBytes;
        }
    }
}
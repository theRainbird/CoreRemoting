using System;
using System.Security.Cryptography;

namespace CoreRemoting.Encryption
{
    /// <summary>
    /// Provides methods to implement symmetric AES encryption.
    /// </summary>
    public static class AesEncryption
    {
        private const int IvSizeInBytes = 16;

        private static readonly RandomNumberGenerator _ivGenerator = RandomNumberGenerator.Create();

        // Creating an Aes instance is expensive on .NET Framework (it acquires a new CSP context each time) and
        // encryption runs for every message. Aes instances are not thread-safe, so every thread keeps its own one.
        // Key and IV are passed to CreateEncryptor/CreateDecryptor and are never stored on the instance.
        [ThreadStatic]
        private static Aes _threadAes;

        /// <summary>
        /// Creates a SHA-256 hash of a specified value.
        /// </summary>
        /// <param name="value">Value to be hashed</param>
        /// <returns>SHA-256 hash</returns>
        public static byte[] CreateHash(byte[] value)
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(value);
        }

        /// <summary>
        /// Generates an initialization vector.
        /// </summary>
        /// <returns>Initialization vector</returns>
        public static byte[] GenerateIv()
        {
            var iv = new byte[IvSizeInBytes];
            _ivGenerator.GetBytes(iv);
            return iv;
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
            using var encryptor = GetThreadAes().CreateEncryptor(CreateHash(sharedSecret), iv);
            return encryptor.TransformFinalBlock(dataToEncrypt, 0, dataToEncrypt.Length);
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
            using var decryptor = GetThreadAes().CreateDecryptor(CreateHash(sharedSecret), iv);
            return decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);
        }

        private static Aes GetThreadAes()
        {
            if (_threadAes != null)
                return _threadAes;

            var aes = Aes.Create() ?? throw new NotSupportedException();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            _threadAes = aes;
            return aes;
        }
    }
}

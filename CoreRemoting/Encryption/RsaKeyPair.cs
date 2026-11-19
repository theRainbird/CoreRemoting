using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace CoreRemoting.Encryption
{
    /// <summary>
    /// Describes an RSA key pair.
    /// </summary>
    public class RsaKeyPair : IDisposable
    {
        private readonly RSACryptoServiceProvider _rsa;   
        
        /// <summary>
        /// Creates a new instance of the RsaKeyPair.
        /// </summary>
        /// <param name="keySize">Key size</param>
        public RsaKeyPair(int keySize)
        {
            _rsa = new RSACryptoServiceProvider(dwKeySize: keySize);
        }

        /// <summary>
        /// Creates a new instance of the RsaKeyPair.
        /// </summary>
        /// <param name="keySize">Key size</param>
        /// <param name="privateKey">Private key to import</param>
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public RsaKeyPair(int keySize, byte[] privateKey) : this(keySize)
        {
            _rsa.ImportCspBlob(privateKey);
        }

        /// <summary>
        /// Gets the private RSA key.
        /// </summary>
        public byte[] PrivateKey => _rsa.ExportCspBlob(includePrivateParameters: true);
        
        /// <summary>
        /// Gets the public RSA key.
        /// </summary>
        public byte[] PublicKey => _rsa.ExportCspBlob(includePrivateParameters: false);

        /// <summary>
        /// Gets the key size.
        /// </summary>
        public int KeySize => _rsa.KeySize;

        /// <summary>
        /// Creates a PKCS#1 v1.5 signature from the SHA256 hash value of the specified raw data with the private key
        /// of this key pair. The signature is identical to the one created by
        /// <see cref="RsaSignature.CreateSignature"/>, but the private key is neither exported nor imported again,
        /// which makes this method considerably faster when called for every message.
        /// </summary>
        /// <param name="rawData">Raw data to create a signature of</param>
        /// <returns>Signature</returns>
        public byte[] CreateSignature(byte[] rawData)
        {
            if (rawData == null)
                throw new ArgumentNullException(nameof(rawData));

            // RSACryptoServiceProvider instances are not guaranteed to be thread-safe,
            // but a key pair is shared by all messages of a client or session.
            lock (_rsa)
            {
                return _rsa.SignData(rawData, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }

        /// <summary>
        /// Frees managed resources.
        /// </summary>
        public void Dispose()
        {
            _rsa?.Dispose();
        }
    }
}
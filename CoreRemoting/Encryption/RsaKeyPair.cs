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
        /// Frees managed resources.
        /// </summary>
        public void Dispose()
        {
            _rsa?.Dispose();
        }
    }
}
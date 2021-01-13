using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace CoreRemoting.Encryption
{
    public class RsaKeyPair : IDisposable
    {
        private readonly RSACryptoServiceProvider _rsa;   
        
        public RsaKeyPair(int keySize)
        {
            _rsa = new RSACryptoServiceProvider(dwKeySize: keySize);
        }

        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public RsaKeyPair(int keySize, byte[] privateKey) : this(keySize)
        {
            _rsa.ImportCspBlob(privateKey);
        }

        public byte[] PrivateKey => _rsa.ExportCspBlob(includePrivateParameters: true);
        
        public byte[] PublicKey => _rsa.ExportCspBlob(includePrivateParameters: false);

        public void Dispose()
        {
            _rsa?.Dispose();
        }
    }
}
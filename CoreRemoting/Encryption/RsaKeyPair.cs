using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace CoreRemoting.Encryption;

/// <summary>
/// Describes an RSA key pair.
/// </summary>
public class RsaKeyPair : ISessionKeyPair
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
    /// <param name="keyBlob">Private or public key blob to import</param>
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public RsaKeyPair(int keySize, byte[] keyBlob) : this(keySize)
    {
        _rsa.ImportCspBlob(keyBlob);
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
    /// <exception cref="CryptographicException">If this key pair does not contain a private key.</exception>
    public byte[] CreateSignature(byte[] rawData)
    {
        if (rawData == null)
            throw new ArgumentNullException(nameof(rawData));

        // RSACryptoServiceProvider instances are not guaranteed to be thread-safe,
        // but a key pair is shared by all messages of a client or session.
        lock (_rsa)
        {
            try
            {
                return _rsa.SignData(rawData, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch (CryptographicException ex)
            {
                // Normalize platform-specific exceptions to a plain CryptographicException,
                // because on Linux the OpenSSL backend throws OpenSslCryptographicException
                // (a subclass) instead, e.g. when signing with a public-only key pair.
                // This mirrors the exception handling of EcdsaSessionKeyPair.
                throw new CryptographicException(ex.Message, ex);
            }
        }
    }

    /// <summary>
    /// Verifies a signature by delegating to <see cref="RsaSignature.VerifySignature"/>.
    /// </summary>
    public bool VerifySignature(byte[] data, byte[] signature) =>
        RsaSignature.VerifySignature(KeySize, PublicKey, data, signature);

    /// <summary>
    /// Frees managed resources.
    /// </summary>
    public void Dispose() => _rsa?.Dispose();
}
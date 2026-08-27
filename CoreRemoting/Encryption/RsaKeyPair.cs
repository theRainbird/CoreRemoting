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
    private readonly int _keySize;

    /// <summary>
    /// Creates a new instance of the RsaKeyPair.
    /// </summary>
    /// <param name="keySize">Key size</param>
    public RsaKeyPair(int keySize)
    {
        _keySize = keySize;
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
    public int KeySize => _keySize;

    /// <summary>
    /// Signs the given data by delegating to <see cref="RsaSignature.CreateSignature"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">If this instance has no private key.</exception>
    public byte[] CreateSignature(byte[] data) =>
        RsaSignature.CreateSignature(_keySize, PrivateKey, data);

    /// <summary>
    /// Verifies a signature by delegating to <see cref="RsaSignature.VerifySignature"/>.
    /// </summary>
    public bool VerifySignature(byte[] data, byte[] signature) =>
        RsaSignature.VerifySignature(_keySize, PublicKey, data, signature);

    /// <summary>
    /// Frees managed resources.
    /// </summary>
    public void Dispose() => _rsa?.Dispose();
}
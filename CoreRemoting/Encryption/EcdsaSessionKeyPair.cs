using System;
using System.Security.Cryptography;

namespace CoreRemoting.Encryption;

/// <summary>
/// ECDSA-based session key pair using NIST P-256 curve.
/// Keys are serialized in compact uncompressed point format (65 bytes public, 97 bytes private).
/// </summary>
public sealed class EcdsaSessionKeyPair : ISessionKeyPair
{
    private readonly ECDsa _ecdsa;
    private readonly bool _hasPrivateKey;

    /// <summary>
    /// Generates a new ECDSA P-256 key pair.
    /// </summary>
    public EcdsaSessionKeyPair()
    {
        _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _hasPrivateKey = true;
    }

    /// <summary>
    /// Recreates a full key pair from a serialized private key.
    /// </summary>
    public EcdsaSessionKeyPair(byte[] privateKey)
    {
        _ecdsa = ECDsa.Create();
        _ecdsa.ImportPrivateKey(privateKey);
        _hasPrivateKey = true;
    }

    private EcdsaSessionKeyPair(ECDsa ecdsa, bool hasPrivateKey)
    {
        _ecdsa = ecdsa;
        _hasPrivateKey = hasPrivateKey;
    }

    /// <summary>
    /// Creates a public-key-only verifier from a serialized public key.
    /// </summary>
    public static EcdsaSessionKeyPair FromPublicKey(byte[] publicKey)
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportPublicKey(publicKey);
        return new EcdsaSessionKeyPair(ecdsa, hasPrivateKey: false);
    }

    /// <inheritdoc/>
    public byte[] PublicKey => _ecdsa.ExportPublicKey();

    /// <inheritdoc/>
    public byte[] Sign(byte[] data)
    {
        if (!_hasPrivateKey)
            throw new InvalidOperationException("This instance does not contain a private key.");
        return _ecdsa.SignData(data, HashAlgorithmName.SHA256);
    }

    /// <inheritdoc/>
    public bool Verify(byte[] data, byte[] signature) =>
        _ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);

    /// <inheritdoc/>
    public byte[] ExportPrivateKey()
    {
        if (!_hasPrivateKey)
            throw new InvalidOperationException("This instance does not contain a private key.");
        return _ecdsa.ExportPrivateKey();
    }

    /// <inheritdoc/>
    public void Dispose() => _ecdsa?.Dispose();
}
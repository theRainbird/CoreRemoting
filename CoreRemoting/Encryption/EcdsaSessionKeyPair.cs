using System;
using System.Security.Cryptography;

namespace CoreRemoting.Encryption;

/// <summary>
/// ECDSA-based session key pair using NIST P-256 curve.
/// Significantly lighter than RSA: ~65 bytes public key, ~32 bytes private key.
/// Available in .NET Standard 2.0.
/// </summary>
public sealed class EcdsaSessionKeyPair : ISessionKeyPair
{
    private readonly ECDsa _ecdsa;
    private readonly bool _hasPrivateKey;

    /// <summary>
    /// Generates a new ECDSA key pair on the NIST P-256 curve.
    /// </summary>
    public EcdsaSessionKeyPair()
    {
        _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _hasPrivateKey = true;
    }

    /// <summary>
    /// Recreates a key pair from a private key blob.
    /// </summary>
    public EcdsaSessionKeyPair(byte[] privateKey)
    {
        _ecdsa = ECDsa.Create();
        var parameters = EcdsaKeySerializer.DecodePrivateKey(privateKey);
        _ecdsa.ImportParameters(parameters);
        _hasPrivateKey = true;
    }

    private EcdsaSessionKeyPair(ECDsa ecdsa, bool hasPrivateKey)
    {
        _ecdsa = ecdsa;
        _hasPrivateKey = hasPrivateKey;
    }

    /// <summary>
    /// Creates a public-key-only validator.
    /// </summary>
    public static EcdsaSessionKeyPair FromPublicKey(byte[] publicKey)
    {
        var ecdsa = ECDsa.Create();
        var parameters = EcdsaKeySerializer.DecodePublicKey(publicKey);
        ecdsa.ImportParameters(parameters);
        return new EcdsaSessionKeyPair(ecdsa, hasPrivateKey: false);
    }

    public byte[] PublicKey =>
        EcdsaKeySerializer.EncodePublicKey(_ecdsa.ExportParameters(includePrivateParameters: false));

    public byte[] Sign(byte[] data)
    {
        if (!_hasPrivateKey)
            throw new InvalidOperationException("This instance does not contain a private key.");
        return _ecdsa.SignData(data, HashAlgorithmName.SHA256);
    }

    public bool Verify(byte[] data, byte[] signature) =>
        _ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);

    public byte[] ExportPrivateKey()
    {
        if (!_hasPrivateKey)
            throw new InvalidOperationException("This instance does not contain a private key.");
        return EcdsaKeySerializer.EncodePrivateKey(_ecdsa.ExportParameters(includePrivateParameters: true));
    }

    public void Dispose() => _ecdsa?.Dispose();
}
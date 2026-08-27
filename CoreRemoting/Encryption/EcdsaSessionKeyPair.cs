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
    private byte[] _publicKey;
    private byte[] _privateKey;

    /// <summary>
    /// Generates a new ECDSA P-256 key pair.
    /// </summary>
    public EcdsaSessionKeyPair()
    {
        // Keys will be exported on first access via properties
        _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    }

    /// <summary>
    /// Recreates a full key pair from a serialized private key.
    /// </summary>
    /// <param name="privateKey">Serialized private key in compact format (97 bytes).</param>
    public EcdsaSessionKeyPair(byte[] privateKey)
    {
        _ecdsa = ECDsa.Create();
        _ecdsa.ImportPrivateKey(privateKey);
        _privateKey = privateKey;
    }

    private EcdsaSessionKeyPair(ECDsa ecdsa, byte[] publicKey = null)
    {
        _ecdsa = ecdsa;
        _publicKey = publicKey;
    }

    /// <summary>
    /// Creates a public-key-only verifier from a serialized public key.
    /// </summary>
    /// <param name="publicKey">Serialized public key in compact format (65 bytes).</param>
    /// <returns>A key pair instance that can only verify signatures.</returns>
    public static EcdsaSessionKeyPair FromPublicKey(byte[] publicKey)
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportPublicKey(publicKey);
        return new EcdsaSessionKeyPair(ecdsa, publicKey);
    }

    /// <inheritdoc/>
    public byte[] PublicKey => _publicKey ??= _ecdsa.ExportPublicKey();

    /// <inheritdoc/>
    public byte[] PrivateKey => _privateKey ??= _ecdsa.ExportPrivateKey();

    /// <inheritdoc/>
    public byte[] CreateSignature(byte[] data) =>
        WrapException(() => _ecdsa.SignData(data, HashAlgorithmName.SHA256));

    /// <inheritdoc/>
    public bool VerifySignature(byte[] data, byte[] signature) =>
        _ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);

    /// <inheritdoc/>
    public void Dispose() => _ecdsa?.Dispose();

    private static T WrapException<T>(Func<T> function)
    {
        try
        {
            return function();
        }
        catch (Exception ex)
        {
            throw new CryptographicException(ex.Message, ex);
        }
    }
}
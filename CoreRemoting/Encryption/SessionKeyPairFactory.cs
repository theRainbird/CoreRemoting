using System;

namespace CoreRemoting.Encryption;

/// <summary>
/// Factory for creating <see cref="ISessionKeyPair"/> instances.
/// Supports RSA and ECDSA P-256 algorithms.
/// </summary>
public static class SessionKeyPairFactory
{
    /// <summary>
    /// Generates a new RSA session key pair with a freshly generated key.
    /// </summary>
    /// <param name="keySize">Key size in bits (1024, 2048, 4096, ...). Defaults to 2048.</param>
    public static ISessionKeyPair GenerateRsa(int keySize = 2048) =>
        new RsaKeyPair(keySize);

    /// <summary>
    /// Generates a new ECDSA session key pair using the NIST P-256 curve.
    /// </summary>
    public static ISessionKeyPair GenerateEcdsa() =>
        new EcdsaSessionKeyPair();

    /// <summary>
    /// Creates a session key pair from a public key blob, auto-detecting the algorithm.
    /// <para>
    /// Detection is simple: ECDSA P-256 blobs are exactly 65 bytes starting with 0x04
    /// (uncompressed point marker). Everything else is treated as an RSA CSP PUBLICKEYBLOB,
    /// and the underlying RSA implementation will throw <see cref="System.Security.Cryptography.CryptographicException"/>
    /// if the blob is malformed.
    /// </para>
    /// </summary>
    /// <param name="publicKey">Public key blob in either ECDSA compact or RSA CSP format.</param>
    /// <returns>A session key pair suitable for server-side signature verification.</returns>
    /// <exception cref="ArgumentException">If <paramref name="publicKey"/> is null or empty.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">If the blob is malformed RSA.</exception>
    public static ISessionKeyPair FromPublicKey(byte[] publicKey)
    {
        if (publicKey is null or { Length: 0 })
            throw new ArgumentException("Key blob cannot be null or empty.", nameof(publicKey));

        if (IsEcdsaFormat(publicKey, EcdsaKeySerializer.PublicKeyLength))
            return EcdsaSessionKeyPair.FromPublicKey(publicKey);

        // RSA CSP PUBLICKEYBLOB: keySize is at offset 12 as little-endian int32
        var keySize = BitConverter.ToInt32(publicKey, 12);
        return new RsaKeyPair(keySize, publicKey);
    }

    /// <summary>
    /// Creates a session key pair from a private key blob, auto-detecting the algorithm.
    /// <para>
    /// Detection is simple: ECDSA P-256 private blobs are exactly 97 bytes starting with 0x04.
    /// Everything else is treated as an RSA CSP PRIVATEKEYBLOB, and the underlying RSA
    /// implementation will throw <see cref="System.Security.Cryptography.CryptographicException"/>
    /// if the blob is malformed.
    /// </para>
    /// </summary>
    /// <param name="privateKey">Private key blob in either ECDSA compact or RSA CSP format.</param>
    /// <returns>A session key pair suitable for client-side signature creation during reconnection.</returns>
    /// <exception cref="ArgumentException">If <paramref name="privateKey"/> is null or empty.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">If the blob is malformed RSA.</exception>
    public static ISessionKeyPair FromPrivateKey(byte[] privateKey)
    {
        if (privateKey == null || privateKey.Length == 0)
            throw new ArgumentException("Key blob cannot be null or empty.", nameof(privateKey));

        if (IsEcdsaFormat(privateKey, EcdsaKeySerializer.PrivateKeyLength))
            return new EcdsaSessionKeyPair(privateKey);

        // RSA CSP PRIVATEKEYBLOB: keySize is at offset 12 as little-endian int32
        var keySize = BitConverter.ToInt32(privateKey, 12);
        return new RsaKeyPair(keySize, privateKey);
    }

    /// <summary>
    /// Checks whether the blob matches the ECDSA compact format for a given length.
    /// </summary>
    private static bool IsEcdsaFormat(byte[] blob, int expectedLength) =>
        blob.Length == expectedLength && blob[0] == EcdsaKeySerializer.UncompressedPointMarker;
}
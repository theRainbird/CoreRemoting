using System;
using System.Security.Cryptography;

namespace CoreRemoting.Encryption;

/// <summary>
/// Asymmetric key pair for session resumption and message signing.
/// The private key never leaves the owning party.
/// Uses lightweight ECDSA when encryption is disabled, or RSA when encryption is enabled.
/// </summary>
public interface ISessionKeyPair : IDisposable
{
    /// <summary>
    /// Signs the given data with the private key.
    /// </summary>
    /// <param name="data">Data to sign (typically a challenge from the server).</param>
    /// <returns>Signature bytes.</returns>
    /// <exception cref="InvalidOperationException">If no private key is available.</exception>
    byte[] Sign(byte[] data);

    /// <summary>
    /// Verifies a signature against the public key.
    /// </summary>
    /// <param name="data">Data that was signed.</param>
    /// <param name="signature">Signature to verify.</param>
    /// <returns>True if the signature is valid.</returns>
    bool Verify(byte[] data, byte[] signature);

    /// <summary>
    /// Gets the public key.
    /// </summary>
    byte[] PublicKey { get; }

    /// <summary>
    /// Gets the private key for persistence (for client-side reconnection).
    /// </summary>
    /// <exception cref="CryptographicException">If the key pair was created without a private key.</exception>
    byte[] PrivateKey { get; }
}

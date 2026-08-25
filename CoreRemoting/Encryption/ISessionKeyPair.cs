using System;

namespace CoreRemoting.Encryption;

/// <summary>
/// Asymmetric key pair used for session resumption challenge-response authentication.
/// The private key must never leave the client.
/// </summary>
public interface ISessionKeyPair : IDisposable
{
    /// <summary>
    /// Public key in a transportable format.
    /// </summary>
    byte[] PublicKey { get; }

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
    /// Exports the private key for persistence (for client-side reconnection).
    /// </summary>
    byte[] ExportPrivateKey();
}

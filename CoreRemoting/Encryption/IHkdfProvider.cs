/// <summary>
/// Interface for HKDF (RFC 5869) key derivation providers.
/// </summary>
public interface IHkdfProvider
{
    /// <summary>
    /// Gets the hash output length in bytes.
    /// </summary>
    int HashLength { get; }

    /// <summary>
    /// Derives a key of the specified length from the input keying material.
    /// </summary>
    /// <param name="ikm">Input keying material.</param>
    /// <param name="outputLength">Required key length in bytes.</param>
    /// <param name="salt">Optional salt (defaults to zero-filled array of <see cref="HashLength"/>).</param>
    /// <param name="info">Optional context and application-specific information.</param>
    /// <returns>Derived key of the requested length.</returns>
    byte[] DeriveKey(byte[] ikm, int outputLength, byte[] salt = null, byte[] info = null);
}

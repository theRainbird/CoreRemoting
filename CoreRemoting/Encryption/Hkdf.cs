using System;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Provides ready-to-use <see cref="IHkdfProvider"/> instances for common HMAC algorithms,
/// as well as extension methods for convenient key derivation.
/// </summary>
public static class Hkdf
{
    /// <summary>
    /// HKDF provider based on HMAC-SHA256.
    /// </summary>
    public static IHkdfProvider Sha256 { get; } = new Hkdf<HMACSHA256>.Provider();

    /// <summary>
    /// HKDF provider based on HMAC-SHA384.
    /// </summary>
    public static IHkdfProvider Sha384 { get; } = new Hkdf<HMACSHA384>.Provider();

    /// <summary>
    /// HKDF provider based on HMAC-SHA512.
    /// </summary>
    public static IHkdfProvider Sha512 { get; } = new Hkdf<HMACSHA512>.Provider();

    /// <summary>
    /// HKDF provider based on HMAC-SHA1. Provided for legacy scenarios only.
    /// </summary>
    public static IHkdfProvider Sha1 { get; } = new Hkdf<HMACSHA1>.Provider();

#if NET8_0_OR_GREATER

    /// <summary>
    /// HKDF provider based on HMAC-SHA3-256.
    /// </summary>
    public static IHkdfProvider Sha3_256 { get; } = new Hkdf<HMACSHA3_256>.Provider();

    /// <summary>
    /// HKDF provider based on HMAC-SHA3-384.
    /// </summary>
    public static IHkdfProvider Sha3_384 { get; } = new Hkdf<HMACSHA3_384>.Provider();

    /// <summary>
    /// HKDF provider based on HMAC-SHA3-512.
    /// </summary>
    public static IHkdfProvider Sha3_512 { get; } = new Hkdf<HMACSHA3_512>.Provider();

#endif

    /// <summary>
    /// A bypass provider that returns the input key unchanged.
    /// Use when you already have a cryptographically strong key (e.g., from SRP).
    /// </summary>
    public static IHkdfProvider Bypass { get; } = new BypassHkdfProvider();

    /// <summary>
    /// Default HKDF provider (currently <see cref="Sha512"/>).
    /// </summary>
    public static IHkdfProvider Default => Sha512;

    /// <summary>
    /// A bypass HKDF provider that returns the input keying material unchanged.
    /// Salt, info, and outputLength parameters are ignored.
    /// </summary>
    /// <remarks>
    /// Use this provider when you already have a cryptographically strong key
    /// (e.g., from SRP or another key exchange) and want to use it directly
    /// without additional derivation.
    /// </remarks>
    private sealed class BypassHkdfProvider : IHkdfProvider
    {
        /// <summary>
        /// Always returns 0, as this provider does not perform hash-based derivation.
        /// </summary>
        public int HashLength => 0;

        /// <inheritdoc/>
        public byte[] DeriveKey(byte[] ikm, int outputLength, byte[] salt = null, byte[] info = null) =>
            ikm?.Length > 0 ? ikm :
                throw new ArgumentException("Input key material cannot be null or empty.", nameof(ikm));
    }

    // Extension Methods

    /// <summary>
    /// Derives a key using a <see cref="Guid"/> as salt (encoded via <see cref="Guid.ToByteArray()"/>)
    /// and a UTF-8 encoded string as info. If <paramref name="hkdf"/> is null,
    /// <see cref="Default"/> is used. <see cref="Guid.Empty"/> and null/empty info
    /// are treated as absent.
    /// </summary>
    public static byte[] DeriveKey(this IHkdfProvider hkdf,
        byte[] ikm, int outputLength, Guid salt, string info = null)
    {
        hkdf ??= Default;

        var saltBytes = salt == Guid.Empty ? null : salt.ToByteArray();
        var infoBytes = string.IsNullOrEmpty(info) ? null : Encoding.UTF8.GetBytes(info);
        return hkdf.DeriveKey(ikm, outputLength, saltBytes, infoBytes);
    }

    /// <summary>
    /// Derives a key without salt, using a UTF-8 encoded string as info.
    /// If <paramref name="hkdf"/> is null, <see cref="Default"/> is used.
    /// </summary>
    public static byte[] DeriveKey(this IHkdfProvider hkdf,
        byte[] ikm, int outputLength, string info)
    {
        hkdf ??= Default;

        var infoBytes = string.IsNullOrEmpty(info) ? null : Encoding.UTF8.GetBytes(info);
        return hkdf.DeriveKey(ikm, outputLength, null, infoBytes);
    }
}
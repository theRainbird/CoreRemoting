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
    /// Default HKDF provider (currently <see cref="Sha256"/>).
    /// </summary>
    public static IHkdfProvider Default => Sha512;

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
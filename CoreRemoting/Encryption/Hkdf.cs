using System.Security.Cryptography;

/// <summary>
/// Provides ready-to-use <see cref="IHkdfProvider"/> instances for common HMAC algorithms.
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

    /// <summary>
    /// Default HKDF provider (currently <see cref="Sha256"/>).
    /// </summary>
    public static IHkdfProvider Default => Sha256;
}
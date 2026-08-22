using System;
using System.Security.Cryptography;

/// <summary>
/// HKDF (RFC 5869) implementation parameterized by HMAC type.
/// </summary>
/// <typeparam name="THmac">HMAC algorithm type (e.g. <see cref="HMACSHA256"/>, <see cref="HMACSHA512"/>).</typeparam>
public static class Hkdf<THmac> where THmac : HMAC, new()
{
    /// <summary>
    /// Gets the hash output length in bytes for the configured HMAC algorithm.
    /// </summary>
    public static int HashLength { get; } = GetHashLength();

    private static int GetHashLength()
    {
        using var hmac = new THmac();
        var len = hmac.HashSize / 8;
        if (len <= 0)
            throw new NotSupportedException($"{typeof(THmac).Name} has invalid HashSize.");
        return len;
    }

    /// <summary>
    /// Derives a key of the specified length from the input keying material.
    /// </summary>
    /// <param name="ikm">Input keying material.</param>
    /// <param name="outputLength">Required key length in bytes.</param>
    /// <param name="salt">Optional salt (defaults to zero-filled array of <see cref="HashLength"/>).</param>
    /// <param name="info">Optional context and application-specific information.</param>
    /// <returns>Derived key of the requested length.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ikm"/> is null or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="outputLength"/> is non-positive or exceeds 255 * <see cref="HashLength"/>.</exception>
    public static byte[] DeriveKey(byte[] ikm, int outputLength, byte[] salt = null, byte[] info = null)
    {
        if (ikm == null || ikm.Length == 0)
            throw new ArgumentException("IKM cannot be null or empty.", nameof(ikm));
        if (outputLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(outputLength), "Length must be positive.");
        if (outputLength > 255 * HashLength)
            throw new ArgumentOutOfRangeException(nameof(outputLength), $"Maximum length is {255 * HashLength} bytes.");

        salt ??= new byte[HashLength];
        info ??= Array.Empty<byte>();

        // Extract: PRK = HMAC-Hash(salt, IKM)
        byte[] prk;
        using (var hmac = new THmac { Key = salt })
        {
            prk = hmac.ComputeHash(ikm);
        }

        // Expand: OKM = T(1) || T(2) || ... || T(N)
        var n = (outputLength + HashLength - 1) / HashLength;
        var result = new byte[n * HashLength];
        var prev = Array.Empty<byte>();

        using (var hmac = new THmac { Key = prk })
        {
            for (var i = 1; i <= n; i++)
            {
                var input = new byte[prev.Length + info.Length + 1];
                Buffer.BlockCopy(prev, 0, input, 0, prev.Length);
                Buffer.BlockCopy(info, 0, input, prev.Length, info.Length);
                input[input.Length - 1] = (byte)i;

                prev = hmac.ComputeHash(input);
                Buffer.BlockCopy(prev, 0, result, (i - 1) * HashLength, HashLength);
            }
        }

        Array.Resize(ref result, outputLength);
        return result;
    }

    /// <summary>
    /// Provides an <see cref="IHkdfProvider"/> implementation backed by <see cref="Hkdf{THmac}"/>.
    /// </summary>
    public sealed class Provider : IHkdfProvider
    {
        /// <inheritdoc/>
        public int HashLength => Hkdf<THmac>.HashLength;

        /// <inheritdoc/>
        public byte[] DeriveKey(byte[] ikm, int outputLength, byte[] salt = null, byte[] info = null) =>
            Hkdf<THmac>.DeriveKey(ikm, outputLength, salt, info);
    }
}
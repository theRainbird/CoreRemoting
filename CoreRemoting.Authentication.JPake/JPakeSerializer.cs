using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Math;

namespace CoreRemoting.Authentication.JPake;

/// <summary>
/// Helper for serializing and deserializing BigInteger values in J-PAKE protocol,
/// and for deriving session keys from keying material.
/// </summary>
public static class JPakeSerializer
{
    /// <summary>
    /// Serializes one or more BigInteger values to a Base64-encoded string.
    /// </summary>
    public static string Serialize(params BigInteger[] values)
    {
        if (values == null || values.Length == 0)
            return string.Empty;

        if (values.Any(v => v == null))
            throw new ArgumentException("One or more BigInteger values are null.", nameof(values));

        return string.Join(",", values.Select(v => Convert.ToBase64String(v.ToByteArray())));
    }

    /// <summary>
    /// Deserializes a Base64-encoded string to an array of BigInteger values.
    /// </summary>
    public static BigInteger[] Deserialize(string serialized)
    {
        if (string.IsNullOrEmpty(serialized))
            return Array.Empty<BigInteger>();

        return serialized
            .Split(',')
            .Select(s => new BigInteger(Convert.FromBase64String(s)))
            .ToArray();
    }

    /// <summary>
    /// Derives a fixed-length session key from the J-PAKE keying material using HKDF-SHA256.
    /// This ensures the negotiated shared key always has the correct length for AES encryption.
    /// </summary>
    /// <param name="keyingMaterial">Raw keying material from J-PAKE participant.CalculateKeyingMaterial().ToByteArray()</param>
    /// <param name="length">Desired key length in bytes (default 32 for AES-256).</param>
    public static byte[] DeriveSessionKey(byte[] keyingMaterial, int length = 32)
    {
        if (keyingMaterial == null || keyingMaterial.Length == 0)
            throw new ArgumentException("Keying material is empty.", nameof(keyingMaterial));

        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Key length must be positive.");

        var info = Encoding.UTF8.GetBytes("JPake-SessionKey");
        return HkdfSha256(keyingMaterial, info, length);
    }

    /// <summary>
    /// HKDF implementation based on SHA-256, compatible with all .NET versions.
    /// Compliant with RFC 5869.
    /// </summary>
    private static byte[] HkdfSha256(byte[] ikm, byte[] info, int length)
    {
        const int hashLen = 32; // SHA-256 output length

        // Step 1: Extract — PRK = HMAC-SHA256(salt, IKM)
        // If salt is not provided, use a string of HashLen zeros (RFC 5869, Section 2.2)
        byte[] salt = new byte[hashLen];
        byte[] prk;
        using (var hmac = new HMACSHA256(salt))
        {
            prk = hmac.ComputeHash(ikm);
        }

        // Step 2: Expand — T(0) = empty, T(i) = HMAC-SHA256(PRK, T(i-1) || info || i)
        int n = (length + hashLen - 1) / hashLen;
        byte[] result = new byte[n * hashLen];
        byte[] prev = Array.Empty<byte>();

        using var hmac2 = new HMACSHA256(prk);
        for (int i = 1; i <= n; i++)
        {
            var input = new byte[prev.Length + info.Length + 1];
            Buffer.BlockCopy(prev, 0, input, 0, prev.Length);
            Buffer.BlockCopy(info, 0, input, prev.Length, info.Length);
            input[input.Length - 1] = (byte)i;

            prev = hmac2.ComputeHash(input);
            Buffer.BlockCopy(prev, 0, result, (i - 1) * hashLen, hashLen);
        }

        Array.Resize(ref result, length);
        return result;
    }
}

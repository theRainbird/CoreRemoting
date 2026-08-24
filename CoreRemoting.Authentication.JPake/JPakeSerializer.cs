using System;
using System.Linq;
using System.Security.Cryptography;
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
        if (values is null or { Length: 0 })
            return string.Empty;

        if (values.Any(v => v == null))
            throw new ArgumentException("One or more BigInteger values are null.", nameof(values));

        return string.Join(",", values.Select(ToBase64));
    }

    /// <summary>
    /// Deserializes a Base64-encoded string to an array of BigInteger values.
    /// </summary>
    public static BigInteger[] Deserialize(string serialized) =>
        string.IsNullOrEmpty(serialized) ? [] :
            [.. serialized.Split(',').Select(FromBase64)];

    private static string ToBase64(BigInteger v) =>
        Convert.ToBase64String(v.ToByteArray());

    private static BigInteger FromBase64(string s) =>
        new(Convert.FromBase64String(s));
}

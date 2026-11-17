using System;

namespace CoreRemoting.Authentication.Oidc;

/// <summary>
/// Provides base64url encoding and decoding as described in RFC 4648 section 5 (used within JWTs).
/// </summary>
internal static class Base64Url
{
    /// <summary>
    /// Decodes a base64url encoded value.
    /// </summary>
    /// <param name="value">Value to be decoded</param>
    /// <returns>Decoded value</returns>
    public static byte[] Decode(string value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        var base64 = value.Replace('-', '+').Replace('_', '/');

        switch (base64.Length % 4)
        {
            case 1:
                throw new FormatException($"The provided base64url value '{value}' is invalid.");
            case 2:
                base64 += "==";
                break;
            case 3:
                base64 += "=";
                break;
        }

        return Convert.FromBase64String(base64);
    }

    /// <summary>
    /// Encodes a value in base64url.
    /// </summary>
    /// <param name="value">Value to be encoded</param>
    /// <returns>Encoded value</returns>
    public static string Encode(byte[] value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

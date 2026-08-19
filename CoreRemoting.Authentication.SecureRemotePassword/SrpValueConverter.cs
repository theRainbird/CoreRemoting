using System;

namespace CoreRemoting.Authentication.SecureRemotePassword;

/// <summary>
/// Provides value conversions for the SRP-6a protocol.
/// </summary>
internal static class SrpValueConverter
{
    /// <summary>
    /// Converts a hex-encoded string into its byte representation.
    /// </summary>
    /// <param name="hexValue">Hex-encoded string (even length).</param>
    /// <returns>Byte representation of the given value</returns>
    /// <exception cref="ArgumentNullException">Thrown if the given value is null</exception>
    /// <exception cref="FormatException">Thrown if the given value is not a valid hex string</exception>
    public static byte[] FromHex(string hexValue)
    {
        if (hexValue == null)
            throw new ArgumentNullException(nameof(hexValue));

        if ((hexValue.Length & 1) == 1)
            throw new FormatException("The given value must have an even number of characters.");

        var result = new byte[hexValue.Length / 2];

        for (var i = 0; i < result.Length; i++)
            result[i] = Convert.ToByte(hexValue.Substring(i * 2, 2), 16);

        return result;
    }
}

using System;
using System.Security.Cryptography;

namespace CoreRemoting.Encryption;

/// <summary>
/// Serialization utilities for ECDSA P-256 keys in compact uncompressed point format.
/// This is a lightweight, non-standard format optimized for internal session key exchange:
/// public key = 65 bytes, private key = 97 bytes (vs 91 and 121 bytes for SPKI/SEC1).
/// </summary>
public static class EcdsaKeySerializer
{
    private const int CoordinateLength = 32;
    private const byte UncompressedPointMarker = 0x04;

    /// <summary>
    /// Length of the public key.
    /// </summary>
    public const int PublicKeyLength = 1 + CoordinateLength + CoordinateLength; // 65

    /// <summary>
    /// Length of the private key.
    /// </summary>
    public const int PrivateKeyLength = PublicKeyLength + CoordinateLength;     // 97

    /// <summary>
    /// Encodes ECDSA public key to uncompressed point format: [0x04][X:32][Y:32] = 65 bytes.
    /// </summary>
    public static byte[] EncodePublicKey(ECParameters parameters)
    {
        var x = Normalize(parameters.Q.X, CoordinateLength);
        var y = Normalize(parameters.Q.Y, CoordinateLength);

        var result = new byte[PublicKeyLength];
        result[0] = UncompressedPointMarker;
        Buffer.BlockCopy(x, 0, result, 1, CoordinateLength);
        Buffer.BlockCopy(y, 0, result, 1 + CoordinateLength, CoordinateLength);

        return result;
    }

    /// <summary>
    /// Decodes uncompressed point format back to ECParameters.
    /// </summary>
    public static ECParameters DecodePublicKey(byte[] data)
    {
        if (data == null || data.Length != PublicKeyLength || data[0] != UncompressedPointMarker)
            throw new ArgumentException(
                $"Invalid ECDSA public key format. Expected {PublicKeyLength} bytes starting with 0x{UncompressedPointMarker:X2}.",
                nameof(data));

        var x = new byte[CoordinateLength];
        var y = new byte[CoordinateLength];
        Buffer.BlockCopy(data, 1, x, 0, CoordinateLength);
        Buffer.BlockCopy(data, 1 + CoordinateLength, y, 0, CoordinateLength);

        return new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = x, Y = y }
        };
    }

    /// <summary>
    /// Encodes ECDSA private key to compact format: [0x04][X:32][Y:32][D:32] = 97 bytes.
    /// </summary>
    public static byte[] EncodePrivateKey(ECParameters parameters)
    {
        var x = Normalize(parameters.Q.X, CoordinateLength);
        var y = Normalize(parameters.Q.Y, CoordinateLength);
        var d = Normalize(parameters.D, CoordinateLength);

        var result = new byte[PrivateKeyLength];
        result[0] = UncompressedPointMarker;
        Buffer.BlockCopy(x, 0, result, 1, CoordinateLength);
        Buffer.BlockCopy(y, 0, result, 1 + CoordinateLength, CoordinateLength);
        Buffer.BlockCopy(d, 0, result, 1 + 2 * CoordinateLength, CoordinateLength);

        return result;
    }

    /// <summary>
    /// Decodes compact private key format back to ECParameters.
    /// </summary>
    public static ECParameters DecodePrivateKey(byte[] data)
    {
        if (data == null || data.Length != PrivateKeyLength || data[0] != UncompressedPointMarker)
            throw new ArgumentException(
                $"Invalid ECDSA private key format. Expected {PrivateKeyLength} bytes starting with 0x{UncompressedPointMarker:X2}.",
                nameof(data));

        var x = new byte[CoordinateLength];
        var y = new byte[CoordinateLength];
        var d = new byte[CoordinateLength];

        Buffer.BlockCopy(data, 1, x, 0, CoordinateLength);
        Buffer.BlockCopy(data, 1 + CoordinateLength, y, 0, CoordinateLength);
        Buffer.BlockCopy(data, 1 + 2 * CoordinateLength, d, 0, CoordinateLength);

        return new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = x, Y = y },
            D = d
        };
    }

    /// <summary>
    /// Extension: exports the public key of an ECDsa instance in compact format.
    /// </summary>
    public static byte[] ExportPublicKey(this ECDsa ecdsa) =>
        EncodePublicKey(ecdsa.ExportParameters(false));

    /// <summary>
    /// Extension: exports the private key of an ECDsa instance in compact format.
    /// </summary>
    public static byte[] ExportPrivateKey(this ECDsa ecdsa) =>
        EncodePrivateKey(ecdsa.ExportParameters(true));

    /// <summary>
    /// Extension: imports a compact public key into an ECDsa instance.
    /// </summary>
    public static void ImportPublicKey(this ECDsa ecdsa, byte[] data) =>
        ecdsa.ImportParameters(DecodePublicKey(data));

    /// <summary>
    /// Extension: imports a compact private key into an ECDsa instance.
    /// </summary>
    public static void ImportPrivateKey(this ECDsa ecdsa, byte[] data) =>
        ecdsa.ImportParameters(DecodePrivateKey(data));

    /// <summary>
    /// Normalizes a coordinate byte array to exactly <paramref name="targetLength"/> bytes.
    /// Pads with leading zeros if too short, trims leading zeros if too long.
    /// </summary>
    private static byte[] Normalize(byte[] data, int targetLength)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (data.Length == targetLength)
            return data;

        var result = new byte[targetLength];

        if (data.Length > targetLength)
            Buffer.BlockCopy(data, data.Length - targetLength, result, 0, targetLength);
        else
            Buffer.BlockCopy(data, 0, result, targetLength - data.Length, data.Length);

        return result;
    }
}
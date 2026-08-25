using System;
using System.Security.Cryptography;

namespace CoreRemoting.Encryption;

/// <summary>
/// Serialization utilities for ECDSA keys in compact uncompressed point format.
/// </summary>
public static class EcdsaKeySerializer
{
    /// <summary>
    /// Encodes ECDSA public key to uncompressed point format: [0x04][X:32][Y:32] = 65 bytes.
    /// </summary>
    public static byte[] EncodePublicKey(ECParameters parameters)
    {
        var x = parameters.Q.X;
        var y = parameters.Q.Y;

        if (x == null || y == null)
            throw new ArgumentException("Public key point Q is incomplete.", nameof(parameters));

        x = NormalizeLength(x, 32);
        y = NormalizeLength(y, 32);

        var result = new byte[1 + 32 + 32];
        result[0] = 0x04;
        Buffer.BlockCopy(x, 0, result, 1, 32);
        Buffer.BlockCopy(y, 0, result, 33, 32);

        return result;
    }

    /// <summary>
    /// Decodes uncompressed point format back to ECParameters.
    /// </summary>
    public static ECParameters DecodePublicKey(byte[] data)
    {
        if (data == null || data.Length != 65 || data[0] != 0x04)
            throw new ArgumentException(
                "Invalid ECDSA public key format. Expected 65 bytes starting with 0x04.",
                nameof(data));

        var x = new byte[32];
        var y = new byte[32];
        Buffer.BlockCopy(data, 1, x, 0, 32);
        Buffer.BlockCopy(data, 33, y, 0, 32);

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
        var x = parameters.Q.X;
        var y = parameters.Q.Y;
        var d = parameters.D;

        if (x == null || y == null || d == null)
            throw new ArgumentException("Private key is incomplete.", nameof(parameters));

        x = NormalizeLength(x, 32);
        y = NormalizeLength(y, 32);
        d = NormalizeLength(d, 32);

        var result = new byte[1 + 32 + 32 + 32];
        result[0] = 0x04;
        Buffer.BlockCopy(x, 0, result, 1, 32);
        Buffer.BlockCopy(y, 0, result, 33, 32);
        Buffer.BlockCopy(d, 0, result, 65, 32);

        return result;
    }

    /// <summary>
    /// Decodes compact private key format back to ECParameters.
    /// </summary>
    public static ECParameters DecodePrivateKey(byte[] data)
    {
        if (data == null || data.Length != 97 || data[0] != 0x04)
            throw new ArgumentException(
                "Invalid ECDSA private key format. Expected 97 bytes starting with 0x04.",
                nameof(data));

        var x = new byte[32];
        var y = new byte[32];
        var d = new byte[32];

        Buffer.BlockCopy(data, 1, x, 0, 32);
        Buffer.BlockCopy(data, 33, y, 0, 32);
        Buffer.BlockCopy(data, 65, d, 0, 32);

        return new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = x, Y = y },
            D = d
        };
    }

    private static byte[] NormalizeLength(byte[] data, int targetLength)
    {
        if (data.Length == targetLength)
            return data;

        if (data.Length > targetLength)
        {
            var result = new byte[targetLength];
            Buffer.BlockCopy(data, data.Length - targetLength, result, 0, targetLength);
            return result;
        }

        {
            var result = new byte[targetLength];
            Buffer.BlockCopy(data, 0, result, targetLength - data.Length, data.Length);
            return result;
        }
    }
}

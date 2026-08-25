using System;
using System.Security.Cryptography;
using CoreRemoting.Encryption;
using Xunit;

namespace CoreRemoting.Tests.Encryption;

public class EcdsaKeySerializerTests
{
    [Fact]
    public void EncodePublicKey_ProducesCorrectLength()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdsa.ExportParameters(includePrivateParameters: false);

        var encoded = EcdsaKeySerializer.EncodePublicKey(parameters);
        var publicKey = ecdsa.ExportPublicKey();

        Assert.Equal(EcdsaKeySerializer.PublicKeyLength, encoded.Length);
        Assert.Equal(EcdsaKeySerializer.UncompressedPointMarker, encoded[0]);
        Assert.Equal(encoded, publicKey);
    }

    [Fact]
    public void RoundTrip_PreservesSignatureCapability()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var originalParams = ecdsa.ExportParameters(includePrivateParameters: true);

        var encoded = EcdsaKeySerializer.EncodePrivateKey(originalParams);
        var privateKey = ecdsa.ExportPrivateKey();
        Assert.Equal(encoded, privateKey);

        var decoded = EcdsaKeySerializer.DecodePrivateKey(encoded);

        using var ecdsa2 = ECDsa.Create();
        ecdsa2.ImportParameters(decoded);

        var data = new byte[] { 1, 2, 3, 4, 5 };
        var signature = ecdsa.SignData(data, HashAlgorithmName.SHA256);

        Assert.True(ecdsa2.VerifyData(data, signature, HashAlgorithmName.SHA256));

        using var ecdsa3 = ECDsa.Create();
        ecdsa3.ImportPrivateKey(privateKey);

        Assert.True(ecdsa3.VerifyData(data, signature, HashAlgorithmName.SHA256));
    }

    [Fact]
    public void RoundTrip_PublicKey()
    {
        using var original = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var originalParams = original.ExportParameters(includePrivateParameters: false);

        var encoded = EcdsaKeySerializer.EncodePublicKey(originalParams);
        var decoded = EcdsaKeySerializer.DecodePublicKey(encoded);

        Assert.Equal(originalParams.Q.X, decoded.Q.X);
        Assert.Equal(originalParams.Q.Y, decoded.Q.Y);
    }

    [Fact]
    public void EncodePublicKey_NormalizesShortCoordinates()
    {
        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = new byte[30],
                Y = new byte[31]
            }
        };
        parameters.Q.X[0] = 0xAB;
        parameters.Q.Y[0] = 0xCD;

        var encoded = EcdsaKeySerializer.EncodePublicKey(parameters);

        Assert.Equal(EcdsaKeySerializer.PublicKeyLength, encoded.Length);
        Assert.Equal(0x00, encoded[1]);
        Assert.Equal(0x00, encoded[2]);
        Assert.Equal(0xAB, encoded[3]);
    }

    [Fact]
    public void EncodePublicKey_NormalizesLongCoordinates()
    {
        var x = new byte[33];
        x[0] = 0x00;
        x[1] = 0xAB;
        var y = new byte[32];

        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = x, Y = y }
        };

        var encoded = EcdsaKeySerializer.EncodePublicKey(parameters);
        var decoded = EcdsaKeySerializer.DecodePublicKey(encoded);

        Assert.Equal(32, decoded.Q.X.Length);
        Assert.Equal(0xAB, decoded.Q.X[0]);
    }

    [Fact]
    public void RoundTrip_MultipleKeys_HandlesVaryingLengths()
    {
        for (var i = 0; i < 10; i++)
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var parameters = ecdsa.ExportParameters(includePrivateParameters: true);

            var publicEncoded = EcdsaKeySerializer.EncodePublicKey(parameters);
            var privateEncoded = EcdsaKeySerializer.EncodePrivateKey(parameters);

            Assert.Equal(EcdsaKeySerializer.PublicKeyLength, publicEncoded.Length);
            Assert.Equal(EcdsaKeySerializer.PrivateKeyLength, privateEncoded.Length);

            var publicDecoded = EcdsaKeySerializer.DecodePublicKey(publicEncoded);
            var privateDecoded = EcdsaKeySerializer.DecodePrivateKey(privateEncoded);

            Assert.Equal(32, publicDecoded.Q.X.Length);
            Assert.Equal(32, publicDecoded.Q.Y.Length);
            Assert.Equal(32, privateDecoded.D.Length);
        }
    }

    [Fact]
    public void DecodePublicKey_InvalidLength_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            EcdsaKeySerializer.DecodePublicKey(new byte[EcdsaKeySerializer.PublicKeyLength - 1]));
        Assert.Throws<ArgumentException>(() =>
            EcdsaKeySerializer.DecodePublicKey(new byte[EcdsaKeySerializer.PublicKeyLength + 1]));
    }

    [Fact]
    public void DecodePublicKey_InvalidPrefix_ThrowsArgumentException()
    {
        var data = new byte[EcdsaKeySerializer.PublicKeyLength];
        data[0] = 0x03;

        Assert.Throws<ArgumentException>(() => EcdsaKeySerializer.DecodePublicKey(data));
    }

    [Fact]
    public void DecodePrivateKey_InvalidLength_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            EcdsaKeySerializer.DecodePrivateKey(new byte[EcdsaKeySerializer.PrivateKeyLength - 1]));
        Assert.Throws<ArgumentException>(() =>
            EcdsaKeySerializer.DecodePrivateKey(new byte[EcdsaKeySerializer.PrivateKeyLength + 1]));
    }
}

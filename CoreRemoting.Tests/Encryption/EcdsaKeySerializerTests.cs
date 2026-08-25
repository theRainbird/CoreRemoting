using System.Security.Cryptography;
using CoreRemoting.Encryption;
using Xunit;

namespace CoreRemoting.Tests.Encryption;

public class EcdsaKeySerializerTests
{
    [Fact]
    public void EncodePublicKey_Produces65Bytes()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdsa.ExportParameters(includePrivateParameters: false);

        var encoded = EcdsaKeySerializer.EncodePublicKey(parameters);
        var publicKey = ecdsa.ExportPublicKey();

        Assert.Equal(65, encoded.Length);
        Assert.Equal(0x04, encoded[0]);
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
}

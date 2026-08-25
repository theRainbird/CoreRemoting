using System;
using CoreRemoting.Encryption;
using Xunit;

namespace CoreRemoting.Tests.Encryption;

public class EcdsaSessionKeyPairTests
{
    [Fact]
    public void Generate_CreatesValidKeyPair()
    {
        using var keyPair = new EcdsaSessionKeyPair();

        Assert.NotNull(keyPair.PublicKey);
        Assert.Equal(65, keyPair.PublicKey.Length);
        Assert.Equal(0x04, keyPair.PublicKey[0]);
    }

    [Fact]
    public void Sign_WithPrivateKey_ProducesValidSignature()
    {
        using var keyPair = new EcdsaSessionKeyPair();
        var data = new byte[] { 1, 2, 3, 4, 5 };

        var signature = keyPair.Sign(data);

        Assert.NotNull(signature);
        Assert.True(keyPair.Verify(data, signature));
    }

    [Fact]
    public void FromPublicKey_CanVerifySignatures()
    {
        using var keyPair = new EcdsaSessionKeyPair();
        var publicKey = keyPair.PublicKey;
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var signature = keyPair.Sign(data);

        using var verifier = EcdsaSessionKeyPair.FromPublicKey(publicKey);

        Assert.True(verifier.Verify(data, signature));
    }

    [Fact]
    public void ExportPrivateKey_CanRecreateKeyPair()
    {
        using var original = new EcdsaSessionKeyPair();
        var privateKey = original.ExportPrivateKey();
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var signature = original.Sign(data);

        using var recreated = new EcdsaSessionKeyPair(privateKey);

        Assert.True(recreated.Verify(data, signature));
        Assert.Equal(original.PublicKey, recreated.PublicKey);
    }
}
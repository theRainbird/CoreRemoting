using System;
using CoreRemoting.Encryption;
using Xunit;

namespace CoreRemoting.Tests.Encryption;

public class EcdsaSessionKeyPairTests
{
    [Fact]
    public void PublicKey_Is65Bytes()
    {
        using var keyPair = new EcdsaSessionKeyPair();
        Assert.Equal(EcdsaKeySerializer.PublicKeyLength, keyPair.PublicKey.Length);
    }

    [Fact]
    public void ExportPrivateKey_Is97Bytes()
    {
        using var keyPair = new EcdsaSessionKeyPair();
        Assert.Equal(EcdsaKeySerializer.PrivateKeyLength, keyPair.ExportPrivateKey().Length);
    }

    [Fact]
    public void Sign_Verify_Works()
    {
        using var keyPair = new EcdsaSessionKeyPair();
        var data = new byte[] { 1, 2, 3, 4, 5 };

        var signature = keyPair.Sign(data);

        Assert.True(keyPair.Verify(data, signature));
    }

    [Fact]
    public void Sign_DifferentData_ProducesDifferentSignatures()
    {
        using var keyPair = new EcdsaSessionKeyPair();
        var sig1 = keyPair.Sign([1, 2, 3]);
        var sig2 = keyPair.Sign([4, 5, 6]);

        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void Verify_WrongData_ReturnsFalse()
    {
        using var keyPair = new EcdsaSessionKeyPair();
        var signature = keyPair.Sign([1, 2, 3]);

        Assert.False(keyPair.Verify([4, 5, 6], signature));
    }

    [Fact]
    public void FromPublicKey_CanVerify()
    {
        using var signer = new EcdsaSessionKeyPair();
        var data = new byte[] { 1, 2, 3 };
        var signature = signer.Sign(data);

        using var verifier = EcdsaSessionKeyPair.FromPublicKey(signer.PublicKey);

        Assert.True(verifier.Verify(data, signature));
    }

    [Fact]
    public void FromPublicKey_Sign_Throws()
    {
        using var signer = new EcdsaSessionKeyPair();
        using var verifier = EcdsaSessionKeyPair.FromPublicKey(signer.PublicKey);

        Assert.Throws<InvalidOperationException>(() => verifier.Sign([1]));
    }

    [Fact]
    public void ReconstructedFromPrivateKey_CanSign()
    {
        using var original = new EcdsaSessionKeyPair();
        var privateKey = original.ExportPrivateKey();

        using var recreated = new EcdsaSessionKeyPair(privateKey);

        var data = new byte[] { 1, 2, 3 };
        Assert.True(recreated.Verify(data, original.Sign(data)));
        Assert.True(original.Verify(data, recreated.Sign(data)));
    }

    [Fact]
    public void ExportPrivateKey_WithoutPrivateKey_Throws()
    {
        using var signer = new EcdsaSessionKeyPair();
        using var verifier = EcdsaSessionKeyPair.FromPublicKey(signer.PublicKey);

        Assert.Throws<InvalidOperationException>(verifier.ExportPrivateKey);
    }

    [Fact]
    public void PublicKey_IsConsistentAcrossCalls()
    {
        using var keyPair = new EcdsaSessionKeyPair();
        Assert.Equal(keyPair.PublicKey, keyPair.PublicKey);
    }
}
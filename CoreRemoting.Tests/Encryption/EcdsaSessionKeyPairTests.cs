using System;
using System.Security.Cryptography;
using CoreRemoting.Encryption;
using Xunit;

namespace CoreRemoting.Tests.Encryption;

public class EcdsaSessionKeyPairTests
{
    [Fact]
    public void PublicKey_HasProperSize()
    {
        using var keyPair = new EcdsaSessionKeyPair();
        Assert.Equal(EcdsaKeySerializer.PublicKeyLength, keyPair.PublicKey.Length);
    }

    [Fact]
    public void ExportPrivateKey_HasProperSize()
    {
        using var keyPair = new EcdsaSessionKeyPair();
        Assert.Equal(EcdsaKeySerializer.PrivateKeyLength, keyPair.PrivateKey.Length);
    }

    [Fact]
    public void Sign_Verify_Works()
    {
        using var keyPair = new EcdsaSessionKeyPair();
        var data = new byte[] { 1, 2, 3, 4, 5 };

        var signature = keyPair.CreateSignature(data);

        Assert.True(keyPair.VerifySignature(data, signature));
    }

    [Fact]
    public void Sign_DifferentData_ProducesDifferentSignatures()
    {
        using var keyPair = new EcdsaSessionKeyPair();
        var sig1 = keyPair.CreateSignature([1, 2, 3]);
        var sig2 = keyPair.CreateSignature([4, 5, 6]);
        var sig3 = keyPair.CreateSignature([]);

        Assert.NotEqual(sig1, sig2);
        Assert.NotEqual(sig1, sig3);
    }

    [Fact]
    public void Verify_WrongData_ReturnsFalse()
    {
        using var keyPair = new EcdsaSessionKeyPair();
        var signature = keyPair.CreateSignature([1, 2, 3]);

        Assert.False(keyPair.VerifySignature([4, 5, 6], signature));
    }

    [Fact]
    public void FromPublicKey_CanVerify()
    {
        using var signer = new EcdsaSessionKeyPair();
        var data = new byte[] { 1, 2, 3 };
        var signature = signer.CreateSignature(data);

        using var verifier = EcdsaSessionKeyPair.FromPublicKey(signer.PublicKey);

        Assert.True(verifier.VerifySignature(data, signature));
    }

    [Fact]
    public void FromPublicKey_Sign_Throws()
    {
        using var signer = new EcdsaSessionKeyPair();
        using var verifier = EcdsaSessionKeyPair.FromPublicKey(signer.PublicKey);

        Assert.Throws<CryptographicException>(() => verifier.CreateSignature([1]));
    }

    [Fact]
    public void ReconstructedFromPrivateKey_CanSign()
    {
        using var original = new EcdsaSessionKeyPair();
        var privateKey = original.PrivateKey;

        using var recreated = new EcdsaSessionKeyPair(privateKey);

        var data = new byte[] { 1, 2, 3 };
        Assert.True(recreated.VerifySignature(data, original.CreateSignature(data)));
        Assert.True(original.VerifySignature(data, recreated.CreateSignature(data)));
    }

    [Fact]
    public void ExportPrivateKey_WithoutPrivateKey_Throws()
    {
        using var signer = new EcdsaSessionKeyPair();
        using var verifier = EcdsaSessionKeyPair.FromPublicKey(signer.PublicKey);

        Assert.Throws<CryptographicException>(() => verifier.PrivateKey);
    }

    [Fact]
    public void PublicKey_IsConsistentAcrossCalls()
    {
        using var keyPair = new EcdsaSessionKeyPair();
        Assert.Equal(keyPair.PublicKey, keyPair.PublicKey);
    }
}
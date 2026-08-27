using System;
using System.Security.Cryptography;
using CoreRemoting.Encryption;
using Xunit;

namespace CoreRemoting.Tests.Encryption;

public class RsaKeyPairAsSessionKeyPairTests
{
    [Fact]
    public void Sign_ProducesValidSignature()
    {
        using var keyPair = new RsaKeyPair(2048);
        var data = new byte[] { 1, 2, 3, 4, 5 };

        var signature = keyPair.CreateSignature(data);

        Assert.True(keyPair.VerifySignature(data, signature));
    }

    [Fact]
    public void PublicKey_DoesNotThrow_OnPublicOnlyInstance()
    {
        using var signer = new RsaKeyPair(2048);
        using var publicOnly = new RsaKeyPair(signer.KeySize, signer.PublicKey);

        Assert.NotNull(publicOnly.PublicKey);
    }

    [Fact]
    public void PrivateKey_Throws_OnPublicOnlyInstance()
    {
        using var signer = new RsaKeyPair(2048);
        using var publicOnly = new RsaKeyPair(signer.KeySize, signer.PublicKey);

        Assert.Throws<CryptographicException>(() => _ = publicOnly.PrivateKey);
    }

    [Fact]
    public void Sign_Throws_OnPublicOnlyInstance()
    {
        using var signer = new RsaKeyPair(2048);
        using var publicOnly = new RsaKeyPair(signer.KeySize, signer.PublicKey);

        Assert.Throws<CryptographicException>(() => publicOnly.CreateSignature([1]));
    }

    [Fact]
    public void Factory_FromPublicKey_CanVerify()
    {
        using var signer = new RsaKeyPair(2048);
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var signature = signer.CreateSignature(data);

        using var verifier = SessionKeyPairFactory.FromPublicKey(signer.PublicKey);
        Assert.True(verifier.VerifySignature(data, signature));
    }

    [Fact]
    public void PrivateKey_ExportAndRestore_Roundtrip()
    {
        using var original = new RsaKeyPair(2048);
        var savedPrivateKey = original.PrivateKey;

        using var restored = new RsaKeyPair(original.KeySize, savedPrivateKey);

        var data = new byte[] { 1, 2, 3 };
        Assert.True(restored.VerifySignature(data, original.CreateSignature(data)));
        Assert.True(original.VerifySignature(data, restored.CreateSignature(data)));
    }

    [Fact]
    public void Factory_FromPublicKey_ExtractsCorrectKeySize()
    {
        using var original = new RsaKeyPair(4096);
        using var restored = SessionKeyPairFactory.FromPublicKey(original.PublicKey);
        Assert.Equal(4096, ((RsaKeyPair)restored).KeySize);
    }
}

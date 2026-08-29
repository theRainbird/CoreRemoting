using System;
using System.Security.Cryptography;
using CoreRemoting.Encryption;
using Xunit;

namespace CoreRemoting.Tests.Encryption;

/// <summary>
/// Tests for <see cref="SessionKeyPairFactory"/>: generation, auto-detection,
/// round-trips, and error handling for malformed blobs.
/// </summary>
public class SessionKeyPairFactoryTests
{
    #region Generation

    [Fact]
    public void GenerateRsa_CreatesRsaKeyPair()
    {
        using var keyPair = SessionKeyPairFactory.GenerateRsa(2048);

        Assert.IsType<RsaKeyPair>(keyPair);
    }

    [Fact]
    public void GenerateRsa_CanSignAndVerify()
    {
        using var keyPair = SessionKeyPairFactory.GenerateRsa(2048);
        var data = new byte[] { 1, 2, 3 };

        var signature = keyPair.CreateSignature(data);

        Assert.True(keyPair.VerifySignature(data, signature));
    }

    [Fact]
    public void GenerateEcdsa_CreatesEcdsaKeyPair()
    {
        using var keyPair = SessionKeyPairFactory.GenerateEcdsa();

        Assert.IsType<EcdsaSessionKeyPair>(keyPair);
    }

    [Fact]
    public void GenerateEcdsa_CanSignAndVerify()
    {
        using var keyPair = SessionKeyPairFactory.GenerateEcdsa();
        var data = new byte[] { 1, 2, 3 };

        var signature = keyPair.CreateSignature(data);

        Assert.True(keyPair.VerifySignature(data, signature));
    }

    #endregion

    #region FromPublicKey — RSA

    [Fact]
    public void FromPublicKey_Rsa_ReturnsRsaKeyPair()
    {
        using var original = new RsaKeyPair(2048);

        using var restored = SessionKeyPairFactory.FromPublicKey(original.PublicKey);

        Assert.IsType<RsaKeyPair>(restored);
    }

    [Fact]
    public void FromPublicKey_Rsa_ExtractsCorrectKeySize()
    {
        using var original = new RsaKeyPair(4096);

        using var restored = SessionKeyPairFactory.FromPublicKey(original.PublicKey);

        Assert.Equal(4096, ((RsaKeyPair)restored).KeySize);
    }

    [Fact]
    public void FromPublicKey_Rsa_CanVerify()
    {
        using var original = new RsaKeyPair(2048);
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var signature = original.CreateSignature(data);

        using var verifier = SessionKeyPairFactory.FromPublicKey(original.PublicKey);

        Assert.True(verifier.VerifySignature(data, signature));
    }

    #endregion

    #region FromPublicKey — ECDSA

    [Fact]
    public void FromPublicKey_Ecdsa_ReturnsEcdsaKeyPair()
    {
        using var original = new EcdsaSessionKeyPair();

        using var restored = SessionKeyPairFactory.FromPublicKey(original.PublicKey);

        Assert.IsType<EcdsaSessionKeyPair>(restored);
    }

    [Fact]
    public void FromPublicKey_Ecdsa_CanVerify()
    {
        using var original = new EcdsaSessionKeyPair();
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var signature = original.CreateSignature(data);

        using var verifier = SessionKeyPairFactory.FromPublicKey(original.PublicKey);

        Assert.True(verifier.VerifySignature(data, signature));
    }

    #endregion

    #region FromPrivateKey — RSA

    [Fact]
    public void FromPrivateKey_Rsa_ReturnsRsaKeyPair()
    {
        using var original = new RsaKeyPair(2048);

        using var restored = SessionKeyPairFactory.FromPrivateKey(original.PrivateKey);

        Assert.IsType<RsaKeyPair>(restored);
    }

    [Fact]
    public void FromPrivateKey_Rsa_CanSign()
    {
        using var original = new RsaKeyPair(2048);
        var data = new byte[] { 0xCA, 0xFE };
        var originalSignature = original.CreateSignature(data);

        using var restored = SessionKeyPairFactory.FromPrivateKey(original.PrivateKey);
        var restoredSignature = restored.CreateSignature(data);

        Assert.True(original.VerifySignature(data, restoredSignature));
        Assert.True(restored.VerifySignature(data, originalSignature));
    }

    #endregion

    #region FromPrivateKey — ECDSA

    [Fact]
    public void FromPrivateKey_Ecdsa_ReturnsEcdsaKeyPair()
    {
        using var original = new EcdsaSessionKeyPair();

        using var restored = SessionKeyPairFactory.FromPrivateKey(original.PrivateKey);

        Assert.IsType<EcdsaSessionKeyPair>(restored);
    }

    [Fact]
    public void FromPrivateKey_Ecdsa_CanSign()
    {
        using var original = new EcdsaSessionKeyPair();
        var data = new byte[] { 0xCA, 0xFE };
        var originalSignature = original.CreateSignature(data);

        using var restored = SessionKeyPairFactory.FromPrivateKey(original.PrivateKey);
        var restoredSignature = restored.CreateSignature(data);

        Assert.True(original.VerifySignature(data, restoredSignature));
        Assert.True(restored.VerifySignature(data, originalSignature));
    }

    #endregion

    #region Round-trip (full client-server flow)

    [Fact]
    public void RoundTrip_Rsa_ClientSignsServerVerifies()
    {
        // Клиент генерирует пару
        using var clientKey = SessionKeyPairFactory.GenerateRsa(2048);
        var savedPrivate = clientKey.PrivateKey;
        var sentPublic = clientKey.PublicKey;

        // Сервер создаёт верификатор
        using var serverValidator = SessionKeyPairFactory.FromPublicKey(sentPublic);

        // Клиент реконнектится
        using var reconnectedClient = SessionKeyPairFactory.FromPrivateKey(savedPrivate);

        // Challenge-response
        var challenge = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        var signature = reconnectedClient.CreateSignature(challenge);

        Assert.True(serverValidator.VerifySignature(challenge, signature));
    }

    [Fact]
    public void RoundTrip_Ecdsa_ClientSignsServerVerifies()
    {
        // Клиент генерирует пару
        using var clientKey = SessionKeyPairFactory.GenerateEcdsa();
        var savedPrivate = clientKey.PrivateKey;
        var sentPublic = clientKey.PublicKey;

        // Сервер создаёт верификатор
        using var serverValidator = SessionKeyPairFactory.FromPublicKey(sentPublic);

        // Клиент реконнектится
        using var reconnectedClient = SessionKeyPairFactory.FromPrivateKey(savedPrivate);

        // Challenge-response
        var challenge = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        var signature = reconnectedClient.CreateSignature(challenge);

        Assert.True(serverValidator.VerifySignature(challenge, signature));
    }

    #endregion

    #region Error handling — malformed blobs

    [Fact]
    public void FromPublicKey_Null_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => SessionKeyPairFactory.FromPublicKey(null));
    }

    [Fact]
    public void FromPublicKey_Empty_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => SessionKeyPairFactory.FromPublicKey(Array.Empty<byte>()));
    }

    [Fact]
    public void FromPrivateKey_Null_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => SessionKeyPairFactory.FromPrivateKey(null));
    }

    [Fact]
    public void FromPrivateKey_Empty_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => SessionKeyPairFactory.FromPrivateKey(Array.Empty<byte>()));
    }

    [Fact]
    public void FromPublicKey_MalformedBlob_ThrowsCryptographicException()
    {
        // 65 байт (как ECDSA), но не начинается с 0x04 — попадает в RSA-ветку,
        // где ImportCspBlob выбрасывает CryptographicException.
        var bogusBlob = new byte[65];
        bogusBlob[0] = 0xFF;

        Assert.Throws<CryptographicException>(() =>
            SessionKeyPairFactory.FromPublicKey(bogusBlob));
    }

    [Fact]
    public void FromPublicKey_TooShortBlob_Throws()
    {
        // Слишком короткий для извлечения keySize из offset 12 —
        // либо BitConverter, либо ImportCspBlob бросят исключение.
        Assert.ThrowsAny<Exception>(() =>
            SessionKeyPairFactory.FromPublicKey(new byte[10]));
    }

    [Fact]
    public void FromPrivateKey_MalformedBlob_ThrowsCryptographicException()
    {
        // 97 байт (как ECDSA), но не начинается с 0x04 — попадает в RSA-ветку.
        var bogusBlob = new byte[97];
        bogusBlob[0] = 0xFF;

        Assert.Throws<CryptographicException>(() =>
            SessionKeyPairFactory.FromPrivateKey(bogusBlob));
    }

    [Fact]
    public void FromPrivateKey_TooShortBlob_Throws()
    {
        Assert.ThrowsAny<Exception>(() =>
            SessionKeyPairFactory.FromPrivateKey(new byte[10]));
    }

    #endregion

    #region Algorithm auto-detection

    [Fact]
    public void FromPublicKey_AutoDetectsAlgorithm_CorrectlyForMultipleKeys()
    {
        // Проверяем, что автодетект работает стабильно на разных ключах
        for (var i = 0; i < 5; i++)
        {
            using var rsa = SessionKeyPairFactory.GenerateRsa(2048);
            using var ecdsa = SessionKeyPairFactory.GenerateEcdsa();

            using var rsaRestored = SessionKeyPairFactory.FromPublicKey(rsa.PublicKey);
            using var ecdsaRestored = SessionKeyPairFactory.FromPublicKey(ecdsa.PublicKey);

            Assert.IsType<RsaKeyPair>(rsaRestored);
            Assert.IsType<EcdsaSessionKeyPair>(ecdsaRestored);
        }
    }

    [Fact]
    public void FromPrivateKey_AutoDetectsAlgorithm_CorrectlyForMultipleKeys()
    {
        for (var i = 0; i < 5; i++)
        {
            using var rsa = SessionKeyPairFactory.GenerateRsa(2048);
            using var ecdsa = SessionKeyPairFactory.GenerateEcdsa();

            using var rsaRestored = SessionKeyPairFactory.FromPrivateKey(rsa.PrivateKey);
            using var ecdsaRestored = SessionKeyPairFactory.FromPrivateKey(ecdsa.PrivateKey);

            Assert.IsType<RsaKeyPair>(rsaRestored);
            Assert.IsType<EcdsaSessionKeyPair>(ecdsaRestored);
        }
    }

    #endregion
}

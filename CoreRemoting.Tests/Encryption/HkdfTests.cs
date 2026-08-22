using System;
using Xunit;

namespace CoreRemoting.Tests.Encryption;

public class HkdfTests
{
    [Fact]
    public void DeriveKey_Sha256_Rfc5869_TestCase1()
    {
        // Arrange
        var hkdf = Hkdf.Sha256;
        byte[] ikm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        byte[] salt = HexToBytes("000102030405060708090a0b0c");
        byte[] info = HexToBytes("f0f1f2f3f4f5f6f7f8f9");
        int length = 42;

        // Expected OKM from RFC 5869
        byte[] expected = HexToBytes(
            "3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865");

        // Act
        byte[] result = hkdf.DeriveKey(ikm, length, salt, info);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DeriveKey_Sha512_Rfc5869_TestCase1()
    {
        // Arrange
        var hkdf = Hkdf.Sha512;
        byte[] ikm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        byte[] salt = HexToBytes("000102030405060708090a0b0c");
        byte[] info = HexToBytes("f0f1f2f3f4f5f6f7f8f9");
        int length = 42;

        // Expected OKM (SHA-512 variant from community test vectors)
        byte[] expected = HexToBytes(
            "832390086cda71fb47625bb5ceb168e4c8e26a1a16ed34d9fc7fe92c1481579338da362cb8d9f925d7cb");

        // Act
        byte[] result = hkdf.DeriveKey(ikm, length, salt, info);

        // Assert
        Assert.Equal(expected, result);
    }

    // Test Case 2 — longer inputs
    [Fact]
    public void DeriveKey_Sha256_Rfc5869_TestCase2()
    {
        var hkdf = Hkdf.Sha256;
        byte[] ikm = HexToBytes(
            "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f");
        byte[] salt = HexToBytes(
            "606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeaf");
        byte[] info = HexToBytes(
            "b0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff");
        int length = 82;

        byte[] expected = HexToBytes(
            "b11e398dc80327a1c8e7f78c596a49344f012eda2d4efad8a050cc4c19afa97c59045a99cac7827271cb41c65e590e09da3275600c2f09b8367793a9aca41db71ed4af2");

        byte[] result = hkdf.DeriveKey(ikm, length, salt, info);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void DeriveKey_WithNullSalt_Works()
    {
        var hkdf = Hkdf.Sha256;
        byte[] ikm = new byte[] { 0x01, 0x02, 0x03 };

        byte[] result = hkdf.DeriveKey(ikm, 16);

        Assert.NotNull(result);
        Assert.Equal(16, result.Length);
    }

    [Fact]
    public void DeriveKey_WithNullInfo_Works()
    {
        var hkdf = Hkdf.Sha256;
        byte[] ikm = new byte[] { 0x01, 0x02, 0x03 };
        byte[] salt = new byte[32];

        byte[] result = hkdf.DeriveKey(ikm, 16, salt);

        Assert.NotNull(result);
        Assert.Equal(16, result.Length);
    }

    [Fact]
    public void DeriveKey_ThrowsOnEmptyIkm()
    {
        var hkdf = Hkdf.Sha256;

        Assert.Throws<ArgumentException>(() => hkdf.DeriveKey(Array.Empty<byte>(), 16));
        Assert.Throws<ArgumentException>(() => hkdf.DeriveKey(null, 16));
    }

    [Fact]
    public void DeriveKey_ThrowsOnInvalidLength()
    {
        var hkdf = Hkdf.Sha256;
        byte[] ikm = new byte[] { 0x01, 0x02, 0x03 };

        Assert.Throws<ArgumentOutOfRangeException>(() => hkdf.DeriveKey(ikm, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => hkdf.DeriveKey(ikm, -1));
    }

    private static byte[] HexToBytes(string hex)
    {
        hex = hex.Replace(" ", "").Replace("\n", "").Replace("\r", "");
        int length = hex.Length / 2;
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }
}
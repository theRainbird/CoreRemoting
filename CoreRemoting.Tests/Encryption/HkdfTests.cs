using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace CoreRemoting.Tests.Encryption;

[Collection("CoreRemoting")]
public class HkdfTests
{
    // RFC 5869 Test Vectors

    /// <summary>
    /// Source: RFC 5869 Appendix A.1 — Test Case 1 for SHA-256 (basic test with salt and info).
    /// </summary>
    [Fact]
    public void DeriveKey_Sha256_Rfc5869_ShortPayload()
    {
        var hkdf = Hkdf.Sha256;
        var inkm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var salt = HexToBytes("000102030405060708090a0b0c");
        var info = HexToBytes("f0f1f2f3f4f5f6f7f8f9");
        var length = 42;

        var expected = HexToBytes("3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865");
        var result = hkdf.DeriveKey(inkm, length, salt, info);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Source: RFC 5869 Appendix A.2 — Test Case 2 for SHA-256 (long inputs and outputs).
    /// </summary>
    [Fact]
    public void DeriveKey_Sha256_Rfc5869_LongPayload()
    {
        var hkdf = Hkdf.Sha256;
        var inkm = HexToBytes("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f");
        var salt = HexToBytes("606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeaf");
        var info = HexToBytes("b0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff");
        var length = 82;

        var expected = HexToBytes("b11e398dc80327a1c8e7f78c596a49344f012eda2d4efad8a050cc4c19afa97c59045a99cac7827271cb41c65e590e09da3275600c2f09b8367793a9aca3db71cc30c58179ec3e87c14c01d5c1f3434f1d87");
        var result = hkdf.DeriveKey(inkm, length, salt, info);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Source: RFC 5869 Appendix A.3 — Test Case 3 for SHA-256 (zero-length salt and info).
    /// </summary>
    [Fact]
    public void DeriveKey_Sha256_Rfc5869_EmptySaltAndInfo()
    {
        var hkdf = Hkdf.Sha256;
        var inkm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var length = 42;

        var expected = HexToBytes("8da4e775a563c18f715f802a063c5a31b8a11f5c5ee1879ec3454e5f3c738d2d9d201395faa4b61a96c8");
        var result = hkdf.DeriveKey(inkm, length, [], []);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Source: OpenSSL evpkdf_hkdf.txt / Google Wycheproof hkdf_sha512_test.json —
    /// equivalent of RFC 5869 Test Case 1 for SHA-512.
    /// (RFC 5869 itself does not include SHA-512 test vectors.)
    /// </summary>
    [Fact]
    public void DeriveKey_Sha512_KnownVector()
    {
        var hkdf = Hkdf.Sha512;
        var inkm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var salt = HexToBytes("000102030405060708090a0b0c");
        var info = HexToBytes("f0f1f2f3f4f5f6f7f8f9");
        var length = 42;

        var expected = HexToBytes("832390086cda71fb47625bb5ceb168e4c8e26a1a16ed34d9fc7fe92c1481579338da362cb8d9f925d7cb");
        var result = hkdf.DeriveKey(inkm, length, salt, info);
        Assert.Equal(expected, result);
    }

    // Reference Implementation Comparison

    /// <summary>
    /// Bit-for-bit comparison with System.Security.Cryptography.HKDF (.NET reference implementation).
    /// </summary>
    [Theory]
    [MemberData(nameof(GetHkdfProviders))]
    public void DeriveKey_MatchesBuiltInNetImplementation(IHkdfProvider provider, HashAlgorithmName algorithm, int hashLength)
    {
        var inkm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var salt = HexToBytes("000102030405060708090a0b0c");
        var info = HexToBytes("f0f1f2f3f4f5f6f7f8f9");
        var length = hashLength;

        var expected = HKDF.DeriveKey(algorithm, inkm, length, salt, info);
        var result = provider.DeriveKey(inkm, length, salt, info);

        Assert.Equal(expected, result);
    }

    public static IEnumerable<object[]> GetHkdfProviders() =>
    [
        [Hkdf.Sha256, HashAlgorithmName.SHA256, 32],
        [Hkdf.Sha384, HashAlgorithmName.SHA384, 48],
        [Hkdf.Sha512, HashAlgorithmName.SHA512, 64],
        [Hkdf.Sha3_256, HashAlgorithmName.SHA3_256, 32],
        [Hkdf.Sha3_384, HashAlgorithmName.SHA3_384, 48],
        [Hkdf.Sha3_512, HashAlgorithmName.SHA3_512, 64],
    ];

    // RFC 5869 Default Behavior (null/empty semantics)

    /// <summary>
    /// Source: RFC 5869 Section 2.2 — if salt is not provided,
    /// it is set to a string of HashLen zeros.
    /// </summary>
    [Fact]
    public void DeriveKey_Sha256_NullSalt_UsesZerosOfHashLength()
    {
        var hkdf = Hkdf.Sha256;
        var inkm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var length = 42;

        var nullResult = hkdf.DeriveKey(inkm, length, null, []);
        var explicitZeros = hkdf.DeriveKey(inkm, length, new byte[hkdf.HashLength], []);

        Assert.Equal(explicitZeros, nullResult);
    }

    /// <summary>
    /// Source: RFC 5869 Section 2.3 — info is optional and defaults to empty byte array.
    /// </summary>
    [Fact]
    public void DeriveKey_Sha256_NullInfo_UsesEmptyArray()
    {
        var hkdf = Hkdf.Sha256;
        var inkm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var salt = HexToBytes("000102030405060708090a0b0c");
        var length = 42;

        var nullResult = hkdf.DeriveKey(inkm, length, salt, null);
        var explicitEmpty = hkdf.DeriveKey(inkm, length, salt, []);

        Assert.Equal(explicitEmpty, nullResult);
    }

    /// <summary>
    /// Source: RFC 5869 Section 2.2 — call with null salt and null info should succeed.
    /// </summary>
    [Fact]
    public void DeriveKey_WithNullSalt_Works()
    {
        var hkdf = Hkdf.Sha256;
        var inkm = new byte[] { 0x01, 0x02, 0x03 };
        var result = hkdf.DeriveKey(inkm, 16);

        Assert.NotNull(result);
        Assert.Equal(16, result.Length);
    }

    /// <summary>
    /// Source: RFC 5869 Section 2.3 — call with null info should succeed.
    /// </summary>
    [Fact]
    public void DeriveKey_WithNullInfo_Works()
    {
        var hkdf = Hkdf.Sha256;
        var inkm = new byte[] { 0x01, 0x02, 0x03 };
        var salt = new byte[32];
        var result = hkdf.DeriveKey(inkm, 16, salt);

        Assert.NotNull(result);
        Assert.Equal(16, result.Length);
    }

    // Validation Tests

    /// <summary>
    /// Source: implementation contract — empty or null IKM must be rejected.
    /// </summary>
    [Fact]
    public void DeriveKey_ThrowsOnEmptyIkm()
    {
        var hkdf = Hkdf.Sha256;

        Assert.Throws<ArgumentException>(() => hkdf.DeriveKey([], 16));
        Assert.Throws<ArgumentException>(() => hkdf.DeriveKey(null, 16));
    }

    /// <summary>
    /// Source: implementation contract — non-positive output length must be rejected.
    /// </summary>
    [Fact]
    public void DeriveKey_ThrowsOnInvalidLength()
    {
        var hkdf = Hkdf.Sha256;
        var inkm = new byte[] { 0x01, 0x02, 0x03 };

        Assert.Throws<ArgumentOutOfRangeException>(() => hkdf.DeriveKey(inkm, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => hkdf.DeriveKey(inkm, -1));
    }

    /// <summary>
    /// Source: RFC 5869 Section 2.3 — OKM length must not exceed 255 * HashLen bytes.
    /// </summary>
    [Fact]
    public void DeriveKey_ExcessiveLength_ThrowsArgumentOutOfRangeException()
    {
        var ikm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var maxLength = 255 * Hkdf.Sha256.HashLength;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Hkdf.Sha256.DeriveKey(ikm, maxLength + 1));
    }

    /// <summary>
    /// Source: RFC 5869 Section 2.3 — boundary value 255 * HashLen must succeed.
    /// </summary>
    [Fact]
    public void DeriveKey_MaximumAllowedLength_Works()
    {
        var ikm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var maxLength = 255 * Hkdf.Sha256.HashLength;

        var result = Hkdf.Sha256.DeriveKey(ikm, maxLength);
        Assert.Equal(maxLength, result.Length);
    }

    // Bypass Provider

    [Fact]
    public void Bypass_ReturnsIkmUnchanged()
    {
        var ikm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var result = Hkdf.Bypass.DeriveKey(ikm, 16);

        Assert.Same(ikm, result);
    }

    [Fact]
    public void Bypass_IgnoresOutputLength()
    {
        var ikm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var result = Hkdf.Bypass.DeriveKey(ikm, 1000);

        Assert.Same(ikm, result);
    }

    [Fact]
    public void Bypass_IgnoresSaltAndInfo()
    {
        var ikm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var salt = HexToBytes("0001020304050607");
        var info = HexToBytes("f0f1f2f3");

        var result = Hkdf.Bypass.DeriveKey(ikm, 16, salt, info);

        Assert.Same(ikm, result);
    }

    [Fact]
    public void Bypass_HashLength_IsZero()
    {
        Assert.Equal(0, Hkdf.Bypass.HashLength);
    }

    [Fact]
    public void Bypass_ThrowsOnEmptyIkm()
    {
        Assert.Throws<ArgumentException>(() => Hkdf.Bypass.DeriveKey([], 16));
        Assert.Throws<ArgumentException>(() => Hkdf.Bypass.DeriveKey(null, 16));
    }

    // API Consistency

    /// <summary>
    /// Verifies that <see cref="Hkdf{THmac}.Provider"/> produces the same result
    /// as the static <see cref="Hkdf{THmac}.DeriveKey"/> method.
    /// </summary>
    [Fact]
    public void Provider_ProducesSameResultAsStaticMethod()
    {
        var inkm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var salt = HexToBytes("000102030405060708090a0b0c");
        var info = HexToBytes("f0f1f2f3f4f5f6f7f8f9");
        var length = 42;

        var expected = Hkdf.Sha256.DeriveKey(inkm, length, salt, info);
        var provider = new Hkdf<HMACSHA256>.Provider();
        var actual = provider.DeriveKey(inkm, length, salt, info);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies that the <see cref="Hkdf.Sha256"/> shortcut produces the same result
    /// as the direct generic call <see cref="Hkdf{HMACSHA256}.DeriveKey"/>.
    /// </summary>
    [Fact]
    public void Hkdf_Sha256_Shortcut_MatchesDirectCall()
    {
        var inkm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var salt = HexToBytes("000102030405060708090a0b0c");
        var info = HexToBytes("f0f1f2f3f4f5f6f7f8f9");
        var length = 42;

        var expected = Hkdf<HMACSHA256>.DeriveKey(inkm, length, salt, info);
        var actual = Hkdf.Sha256.DeriveKey(inkm, length, salt, info);

        Assert.Equal(expected, actual);
    }

    // Extension Methods: Guid salt + string info

    /// <summary>
    /// Guid salt is converted via <see cref="Guid.ToByteArray"/> and info is UTF-8 encoded.
    /// </summary>
    [Fact]
    public void DeriveKey_Extension_WithGuidSalt_AndStringInfo_Works()
    {
        var inkm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var salt = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var info = "test-context";

        var result = Hkdf.Sha256.DeriveKey(inkm, 32, salt, info);

        Assert.NotNull(result);
        Assert.Equal(32, result.Length);
    }

    /// <summary>
    /// <see cref="Guid.Empty"/> is treated as an absent salt (equivalent to null byte[]).
    /// </summary>
    [Fact]
    public void DeriveKey_Extension_EmptyGuid_EqualsNullSalt()
    {
        var inkm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var info = "test-context";

        var withEmptyGuid = Hkdf.Sha256.DeriveKey(inkm, 32, Guid.Empty, info);
        var withNoSalt = Hkdf.Sha256.DeriveKey(inkm, 32, salt: null, info: Encoding.UTF8.GetBytes(info));

        Assert.Equal(withNoSalt, withEmptyGuid);
    }

    /// <summary>
    /// Extension with explicit Guid matches an explicit call with Guid.ToByteArray() and UTF-8 info.
    /// </summary>
    [Fact]
    public void DeriveKey_Extension_MatchesExplicitCall()
    {
        var inkm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var salt = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var info = "my-context";

        var extension = Hkdf.Sha256.DeriveKey(inkm, 32, salt, info);
        var explicitCall = Hkdf.Sha256.DeriveKey(inkm, 32, salt.ToByteArray(), Encoding.UTF8.GetBytes(info));

        Assert.Equal(explicitCall, extension);
    }

    // Extension Methods: string info only (no salt)

    /// <summary>
    /// Overload with only a string info produces a valid derived key.
    /// </summary>
    [Fact]
    public void DeriveKey_Extension_WithStringInfoOnly_Works()
    {
        var inkm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var info = "session-context-v1";

        var result = Hkdf.Sha256.DeriveKey(inkm, 32, info);

        Assert.NotNull(result);
        Assert.Equal(32, result.Length);
    }

    /// <summary>
    /// String-info-only extension matches an explicit call with null salt and UTF-8 encoded info.
    /// </summary>
    [Fact]
    public void DeriveKey_Extension_StringInfoOnly_MatchesExplicitCall()
    {
        var inkm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var info = "my-context";

        var extension = Hkdf.Sha256.DeriveKey(inkm, 32, info);
        var explicitCall = Hkdf.Sha256.DeriveKey(inkm, 32, null, Encoding.UTF8.GetBytes(info));

        Assert.Equal(explicitCall, extension);
    }

    /// <summary>
    /// Empty string info is treated the same as null (both map to empty byte array).
    /// </summary>
    [Fact]
    public void DeriveKey_Extension_EmptyStringInfo_EqualsNullInfo()
    {
        var inkm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");

        var withNull = Hkdf.Sha256.DeriveKey(inkm, 32, (string)null);
        var withEmpty = Hkdf.Sha256.DeriveKey(inkm, 32, "");

        Assert.Equal(withNull, withEmpty);
    }

    // Extension Methods: null provider fallback

    /// <summary>
    /// Extension methods on a null provider fall back to <see cref="Hkdf.Default"/> (SHA-256).
    /// </summary>
    [Fact]
    public void DeriveKey_Extension_NullProvider_UsesDefault()
    {
        var inkm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");

        var viaNull = default(IHkdfProvider).DeriveKey(inkm, 32, "ctx");
        var viaDefault = Hkdf.Default.DeriveKey(inkm, 32, "ctx");

        Assert.Equal(viaDefault, viaNull);
    }

    /// <summary>
    /// Extension with Guid on a null provider also falls back to <see cref="Hkdf.Default"/>.
    /// </summary>
    [Fact]
    public void DeriveKey_Extension_WithGuid_NullProvider_UsesDefault()
    {
        var inkm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var salt = Guid.NewGuid();

        var viaNull = default(IHkdfProvider).DeriveKey(inkm, 32, salt, "ctx");
        var viaDefault = Hkdf.Default.DeriveKey(inkm, 32, salt, "ctx");

        Assert.Equal(viaDefault, viaNull);
    }

    // HashLength Property

    /// <summary>
    /// Source: FIPS 180-4 (SHA-2) and FIPS 202 (SHA-3) — verifies correct hash output sizes.
    /// </summary>
    [Theory]
    [MemberData(nameof(GetHashLengthProviders))]
    public void HashLength_ReturnsCorrectValue(IHkdfProvider provider, int expectedLength)
    {
        Assert.Equal(expectedLength, provider.HashLength);
    }

    public static IEnumerable<object[]> GetHashLengthProviders() =>
    [
        [Hkdf.Sha256, 32],
        [Hkdf.Sha384, 48],
        [Hkdf.Sha512, 64],
        [Hkdf.Sha3_256, 32],
        [Hkdf.Sha3_384, 48],
        [Hkdf.Sha3_512, 64],
    ];

    // SHA-3 Specific Tests

    /// <summary>
    /// Extension method with Guid salt and string info works on SHA3-256 provider.
    /// </summary>
    [Fact]
    public void DeriveKey_Extension_Sha3_256_WithGuidSalt_Works()
    {
        var inkm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var salt = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var info = "test-context";

        var result = Hkdf.Sha3_256.DeriveKey(inkm, 32, salt, info);

        Assert.NotNull(result);
        Assert.Equal(32, result.Length);
    }

    /// <summary>
    /// Extension method on SHA3-512 produces a different result than on SHA-256
    /// for the same inputs, confirming that distinct algorithms are used.
    /// </summary>
    [Fact]
    public void DeriveKey_Extension_Sha3_DiffersFromSha2()
    {
        var inkm = HexToBytes("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var salt = Guid.NewGuid();
        var info = "test-context";

        var sha2 = Hkdf.Sha256.DeriveKey(inkm, 32, salt, info);
        var sha3 = Hkdf.Sha3_256.DeriveKey(inkm, 32, salt, info);

        Assert.NotEqual(sha2, sha3);
    }

    // Helpers

    private static byte[] HexToBytes(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return [];

        hex = hex.Replace(" ", "").Replace("\n", "").Replace("\r", "");
        return Convert.FromHexString(hex);
    }
}

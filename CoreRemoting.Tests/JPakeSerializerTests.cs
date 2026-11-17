using System;
using CoreRemoting.Authentication.JPake;
using Org.BouncyCastle.Math;
using Xunit;

namespace CoreRemoting.Tests;

public class JPakeSerializerTests
{
    [Fact]
    public void Serialize_NullArray_ReturnsEmptyString()
    {
        var result = JPakeSerializer.Serialize(null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Serialize_EmptyArray_ReturnsEmptyString()
    {
        var result = JPakeSerializer.Serialize([]);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Serialize_ArrayWithNullElement_ThrowsArgumentException()
    {
        var values = new BigInteger[] { new("123"), null!, new("456") };
        Assert.Throws<ArgumentException>(() => JPakeSerializer.Serialize(values));
    }

    [Fact]
    public void Serialize_ValidValues_ReturnsNonEmptyJoinedBase64()
    {
        var values = new BigInteger[] { new("123"), new("456") };
        var result = JPakeSerializer.Serialize(values);

        Assert.NotEmpty(result);
        Assert.Contains(",", result);

        var deserialized = JPakeSerializer.Deserialize(result);
        Assert.Equal(values, deserialized);
    }

    [Fact]
    public void Deserialize_NullOrEmpty_ReturnsEmptyArray()
    {
        var result1 = JPakeSerializer.Deserialize(null);
        Assert.Empty(result1);

        var result2 = JPakeSerializer.Deserialize(string.Empty);
        Assert.Empty(result2);
    }

    [Fact]
    public void Deserialize_ValidString_ReturnsBigIntegerArray()
    {
        var original = new BigInteger[] { new("123"), new("456") };
        var serialized = JPakeSerializer.Serialize(original);
        var result = JPakeSerializer.Deserialize(serialized);

        Assert.Equal(original, result);
    }

    [Fact]
    public void Deserialize_InvalidBase64_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => JPakeSerializer.Deserialize("not-base64"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("100")]
    [InlineData("1000")]
    public void SerializeDeserialize_Roundtrip_WithSingleValue(string value)
    {
        var original = new BigInteger[] { new(value) };
        var serialized = JPakeSerializer.Serialize(original);
        var result = JPakeSerializer.Deserialize(serialized);

        Assert.Single(result);
        Assert.Equal(original[0], result[0]);
    }

    [Fact]
    public void SerializeDeserialize_Roundtrip_WithLargeNumbers()
    {
        var big1 = BigInteger.ValueOf(int.MaxValue).Pow(321);
        var big2 = BigInteger.ValueOf(long.MaxValue).Pow(123);
        var original = new[] { big1, big2 };

        var serialized = JPakeSerializer.Serialize(original);
        var result = JPakeSerializer.Deserialize(serialized);

        Assert.Equal(original, result);
    }
}
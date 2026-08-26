using System;
using CoreRemoting.Channels.Quic;
using Xunit;

namespace CoreRemoting.Tests.Channels.Quic;

public class QuicHandshakeMessageTests
{
    private static byte[] CreateSampleKey()
    {
        var key = new byte[65];
        new Random(42).NextBytes(key);
        key[0] = 0x04;
        return key;
    }

    [Fact]
    public void Roundtrip_AllFields_Works()
    {
        var guid = Guid.NewGuid();
        var key = CreateSampleKey();
        var original = new QuicHandshakeMessage
        {
            MessageEncryption = true,
            ResumableSessionId = guid,
            ClientPublicKey = key
        };

        var data = original.ToByteArray();
        var restored = QuicHandshakeMessage.FromByteArray(data);

        Assert.Equal(original.MessageEncryption, restored.MessageEncryption);
        Assert.Equal(guid, restored.ResumableSessionId);
        Assert.Equal(key, restored.ClientPublicKey);
    }

    [Fact]
    public void Roundtrip_OnlyEncryption_Works()
    {
        var original = new QuicHandshakeMessage { MessageEncryption = true };
        var data = original.ToByteArray();
        var restored = QuicHandshakeMessage.FromByteArray(data);

        Assert.True(restored.MessageEncryption);
        Assert.Null(restored.ResumableSessionId);
        Assert.Null(restored.ClientPublicKey);
    }

    [Fact]
    public void Roundtrip_OnlySessionId_Works()
    {
        var guid = Guid.NewGuid();
        var original = new QuicHandshakeMessage
        {
            MessageEncryption = false,
            ResumableSessionId = guid
        };

        var data = original.ToByteArray();
        var restored = QuicHandshakeMessage.FromByteArray(data);

        Assert.False(restored.MessageEncryption);
        Assert.Equal(guid, restored.ResumableSessionId);
        Assert.Null(restored.ClientPublicKey);
    }

    [Fact]
    public void Roundtrip_OnlyKey_Works()
    {
        var key = new byte[] { 0x01, 0x02, 0x03 };
        var original = new QuicHandshakeMessage
        {
            MessageEncryption = true,
            ClientPublicKey = key
        };

        var data = original.ToByteArray();
        var restored = QuicHandshakeMessage.FromByteArray(data);

        Assert.True(restored.MessageEncryption);
        Assert.Null(restored.ResumableSessionId);
        Assert.Equal(key, restored.ClientPublicKey);
    }

    [Fact]
    public void Roundtrip_OnlyKey_Long_Works()
    {
        var key = CreateSampleKey();
        var original = new QuicHandshakeMessage
        {
            MessageEncryption = false,
            ClientPublicKey = key
        };

        var data = original.ToByteArray();
        var restored = QuicHandshakeMessage.FromByteArray(data);

        Assert.False(restored.MessageEncryption);
        Assert.Null(restored.ResumableSessionId);
        Assert.Equal(key, restored.ClientPublicKey);
    }

    [Fact]
    public void Roundtrip_EmptyKey_IsSkipped()
    {
        var original = new QuicHandshakeMessage
        {
            MessageEncryption = true,
            ClientPublicKey = Array.Empty<byte>()
        };

        var data = original.ToByteArray();
        var restored = QuicHandshakeMessage.FromByteArray(data);

        Assert.True(restored.MessageEncryption);
        Assert.Null(restored.ClientPublicKey);
    }

    [Fact]
    public void Deserialize_NullOrEmpty_ReturnsEmpty()
    {
        var restored1 = QuicHandshakeMessage.FromByteArray(null);
        Assert.False(restored1.MessageEncryption);
        Assert.Null(restored1.ResumableSessionId);
        Assert.Null(restored1.ClientPublicKey);

        var restored2 = QuicHandshakeMessage.FromByteArray(Array.Empty<byte>());
        Assert.False(restored2.MessageEncryption);
        Assert.Null(restored2.ResumableSessionId);
        Assert.Null(restored2.ClientPublicKey);
    }

    [Fact]
    public void Deserialize_TruncatedData_Throws()
    {
        byte[] invalid1 = [0x01, 0x02, 0x01, 0x02, 0x03];
        Assert.Throws<ArgumentOutOfRangeException>(() => QuicHandshakeMessage.FromByteArray(invalid1));

        byte[] invalid2 = [0x01, 0x03];
        Assert.Throws<ArgumentOutOfRangeException>(() => QuicHandshakeMessage.FromByteArray(invalid2));

        byte[] invalid3 = [0x01, 0x03, 0x04, 0x00, 0x00, 0x00];
        Assert.Throws<ArgumentOutOfRangeException>(() => QuicHandshakeMessage.FromByteArray(invalid3));
    }

    [Fact]
    public void Roundtrip_Ordering_Correct()
    {
        var guid = Guid.NewGuid();
        var key = new byte[] { 0xAA, 0xBB };
        var msg = new QuicHandshakeMessage
        {
            MessageEncryption = true,
            ResumableSessionId = guid,
            ClientPublicKey = key
        };

        var data = msg.ToByteArray();

        Assert.Equal(0x01, data[0]);
        Assert.Equal(0x02, data[1]);

        int keyMarkerPos = 1 + 1 + 16;
        Assert.Equal(0x03, data[keyMarkerPos]);
        int len = BitConverter.ToInt32(data, keyMarkerPos + 1);
        Assert.Equal(2, len);
        Assert.Equal(0xAA, data[keyMarkerPos + 1 + 4]);
        Assert.Equal(0xBB, data[keyMarkerPos + 1 + 5]);
    }
}

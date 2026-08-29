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

    private static byte[] CreateSampleSignature()
    {
        var signature = new byte[64];
        new Random(123).NextBytes(signature);
        return signature;
    }

    [Fact]
    public void Roundtrip_AllFields_Works()
    {
        var guid = Guid.NewGuid();
        var key = CreateSampleKey();
        var signature = CreateSampleSignature();
        var original = new QuicHandshakeMessage
        {
            MessageEncryption = true,
            ResumableSessionId = guid,
            SessionSignature = signature,
            ClientPublicKey = key
        };

        var data = original.ToByteArray();
        var restored = QuicHandshakeMessage.FromByteArray(data);

        Assert.Equal(original.MessageEncryption, restored.MessageEncryption);
        Assert.Equal(guid, restored.ResumableSessionId);
        Assert.Equal(signature, restored.SessionSignature);
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
        Assert.Null(restored.SessionSignature);
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
        Assert.Null(restored.SessionSignature);
        Assert.Null(restored.ClientPublicKey);
    }

    [Fact]
    public void Roundtrip_OnlySignature_Works()
    {
        var signature = CreateSampleSignature();
        var original = new QuicHandshakeMessage
        {
            MessageEncryption = false,
            SessionSignature = signature
        };

        var data = original.ToByteArray();
        var restored = QuicHandshakeMessage.FromByteArray(data);

        Assert.False(restored.MessageEncryption);
        Assert.Null(restored.ResumableSessionId);
        Assert.Equal(signature, restored.SessionSignature);
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
        Assert.Null(restored.SessionSignature);
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
        Assert.Null(restored.SessionSignature);
        Assert.Equal(key, restored.ClientPublicKey);
    }

    [Fact]
    public void Roundtrip_SessionIdAndSignature_Works()
    {
        var guid = Guid.NewGuid();
        var signature = CreateSampleSignature();
        var original = new QuicHandshakeMessage
        {
            MessageEncryption = false,
            ResumableSessionId = guid,
            SessionSignature = signature
        };

        var data = original.ToByteArray();
        var restored = QuicHandshakeMessage.FromByteArray(data);

        Assert.False(restored.MessageEncryption);
        Assert.Equal(guid, restored.ResumableSessionId);
        Assert.Equal(signature, restored.SessionSignature);
        Assert.Null(restored.ClientPublicKey);
    }

    [Fact]
    public void Roundtrip_SignatureAndKey_Works()
    {
        var signature = CreateSampleSignature();
        var key = CreateSampleKey();
        var original = new QuicHandshakeMessage
        {
            MessageEncryption = true,
            SessionSignature = signature,
            ClientPublicKey = key
        };

        var data = original.ToByteArray();
        var restored = QuicHandshakeMessage.FromByteArray(data);

        Assert.True(restored.MessageEncryption);
        Assert.Null(restored.ResumableSessionId);
        Assert.Equal(signature, restored.SessionSignature);
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
    public void Roundtrip_EmptySignature_IsSkipped()
    {
        var original = new QuicHandshakeMessage
        {
            MessageEncryption = true,
            SessionSignature = Array.Empty<byte>()
        };

        var data = original.ToByteArray();
        var restored = QuicHandshakeMessage.FromByteArray(data);

        Assert.True(restored.MessageEncryption);
        Assert.Null(restored.SessionSignature);
    }

    [Fact]
    public void Deserialize_NullOrEmpty_ReturnsEmpty()
    {
        var restored1 = QuicHandshakeMessage.FromByteArray(null);
        Assert.False(restored1.MessageEncryption);
        Assert.Null(restored1.ResumableSessionId);
        Assert.Null(restored1.SessionSignature);
        Assert.Null(restored1.ClientPublicKey);

        var restored2 = QuicHandshakeMessage.FromByteArray(Array.Empty<byte>());
        Assert.False(restored2.MessageEncryption);
        Assert.Null(restored2.ResumableSessionId);
        Assert.Null(restored2.SessionSignature);
        Assert.Null(restored2.ClientPublicKey);
    }

    [Fact]
    public void Deserialize_TruncatedData_Throws()
    {
        // Truncated SessionId
        byte[] invalid1 = [0x01, 0x02, 0x01, 0x02, 0x03];
        Assert.Throws<ArgumentOutOfRangeException>(() => QuicHandshakeMessage.FromByteArray(invalid1));

        // Truncated PublicKey marker without length
        byte[] invalid2 = [0x01, 0x03];
        Assert.Throws<ArgumentOutOfRangeException>(() => QuicHandshakeMessage.FromByteArray(invalid2));

        // Truncated PublicKey length
        byte[] invalid3 = [0x01, 0x03, 0x04, 0x00, 0x00, 0x00];
        Assert.Throws<ArgumentOutOfRangeException>(() => QuicHandshakeMessage.FromByteArray(invalid3));

        // Truncated Signature marker without length
        byte[] invalid4 = [0x01, 0x04];
        Assert.Throws<ArgumentOutOfRangeException>(() => QuicHandshakeMessage.FromByteArray(invalid4));

        // Truncated Signature data
        byte[] invalid5 = [0x01, 0x04, 0x10, 0x00, 0x00, 0x00, 0xAA, 0xBB];
        Assert.Throws<ArgumentOutOfRangeException>(() => QuicHandshakeMessage.FromByteArray(invalid5));
    }

    [Fact]
    public void Roundtrip_Ordering_Correct()
    {
        var guid = Guid.NewGuid();
        var signature = new byte[] { 0xCC, 0xDD, 0xEE };
        var key = new byte[] { 0xAA, 0xBB };
        var msg = new QuicHandshakeMessage
        {
            MessageEncryption = true,
            ResumableSessionId = guid,
            SessionSignature = signature,
            ClientPublicKey = key
        };

        var data = msg.ToByteArray();

        // Флаг шифрования
        Assert.Equal(0x01, data[0]);

        // SessionId marker
        Assert.Equal(0x02, data[1]);

        // Signature marker (после SessionId: 1 + 1 + 16 = 18)
        int signatureMarkerPos = 1 + 1 + 16;
        Assert.Equal(0x04, data[signatureMarkerPos]);
        int signatureLen = BitConverter.ToInt32(data, signatureMarkerPos + 1);
        Assert.Equal(3, signatureLen);
        Assert.Equal(0xCC, data[signatureMarkerPos + 1 + 4]);
        Assert.Equal(0xDD, data[signatureMarkerPos + 1 + 5]);
        Assert.Equal(0xEE, data[signatureMarkerPos + 1 + 6]);

        // PublicKey marker (после Signature: 18 + 1 + 4 + 3 = 26)
        int keyMarkerPos = signatureMarkerPos + 1 + 4 + signatureLen;
        Assert.Equal(0x03, data[keyMarkerPos]);
        int keyLen = BitConverter.ToInt32(data, keyMarkerPos + 1);
        Assert.Equal(2, keyLen);
        Assert.Equal(0xAA, data[keyMarkerPos + 1 + 4]);
        Assert.Equal(0xBB, data[keyMarkerPos + 1 + 5]);
    }
}
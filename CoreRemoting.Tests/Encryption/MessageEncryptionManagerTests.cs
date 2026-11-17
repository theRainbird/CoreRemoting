using System;
using System.Security;
using CoreRemoting.Encryption;
using CoreRemoting.RpcMessaging;
using CoreRemoting.Serialization;
using CoreRemoting.Serialization.NeoBinary;
using Xunit;

namespace CoreRemoting.Tests.Encryption;

public class MessageEncryptionManagerTests
{
    private readonly MessageEncryptionManager _manager = new();

    private readonly ISerializerAdapter _serializer = new NeoBinarySerializerAdapter();

    private static byte[] GetSampleData() => [0x01, 0x02, 0x03, 0x04, 0x05];

    private static byte[] GetSharedSecret() => new byte[32]; // all zeros

    [Fact]
    public void CreateWireMessage_EmptyMessageType_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            _manager.CreateWireMessage("", GetSampleData(), _serializer));

        Assert.Throws<ArgumentException>(() =>
            _manager.CreateWireMessage("   ", GetSampleData(), _serializer));
    }

    [Fact]
    public void CreateWireMessage_NoEncryptionNoSigning_Works()
    {
        var data = GetSampleData();
        var wire = _manager.CreateWireMessage(
            "TestType",
            data,
            _serializer,
            keyPair: null,
            sharedSecret: null);

        Assert.Equal("TestType", wire.MessageType);
        Assert.Equal(data, wire.Data);
        Assert.Empty(wire.Iv);
        Assert.False(wire.Error);
        Assert.Null(wire.UniqueCallKey);
    }

    [Fact]
    public void CreateWireMessage_WithEncryptionAndSigning_Works()
    {
        using var keyPair = SessionKeyPairFactory.GenerateEcdsa();
        var secret = GetSharedSecret();
        var data = GetSampleData();
        var wire = _manager.CreateWireMessage(
            "TestType",
            data,
            _serializer,
            keyPair: keyPair,
            sharedSecret: secret);

        Assert.Equal("TestType", wire.MessageType);
        Assert.NotEqual(data, wire.Data);
        Assert.NotEmpty(wire.Iv);

        var decrypted = _manager.GetDecryptedMessageData(
            wire,
            _serializer,
            secret,
            sendersPublicKeyBlob: keyPair.PublicKey);

        Assert.Equal(data, decrypted);
    }

    [Fact]
    public void CreateWireMessage_SetsErrorAndUniqueCallKey()
    {
        var callKey = new byte[] { 0xAA, 0xBB };
        var data = GetSampleData();
        var wire = _manager.CreateWireMessage(
            "TestType",
            data,
            _serializer,
            keyPair: null,
            sharedSecret: null,
            error: true,
            uniqueCallKey: callKey);

        Assert.True(wire.Error);
        Assert.Equal(callKey, wire.UniqueCallKey);
    }

    [Fact]
    public void GetDecryptedMessageData_NotEncrypted_ReturnsOriginal()
    {
        var data = GetSampleData();
        var wire = new WireMessage
        {
            MessageType = "Test",
            Data = data,
            Iv = Array.Empty<byte>()
        };

        var result = _manager.GetDecryptedMessageData(wire, _serializer);
        Assert.Equal(data, result);
    }

    [Fact]
    public void GetDecryptedMessageData_DecryptWithoutVerification_Works()
    {
        var secret = GetSharedSecret();
        var data = GetSampleData();
        var wire = _manager.CreateWireMessage("Test", data, _serializer, sharedSecret: secret);

        var result = _manager.GetDecryptedMessageData(wire, _serializer, secret);
        Assert.Equal(data, result);
    }

    [Fact]
    public void GetDecryptedMessageData_DecryptWithVerification_Works()
    {
        using var keyPair = SessionKeyPairFactory.GenerateEcdsa();
        var secret = GetSharedSecret();
        var data = GetSampleData();

        var wire = _manager.CreateWireMessage(
            "Test",
            data,
            _serializer,
            keyPair: keyPair,
            sharedSecret: secret);

        var result = _manager.GetDecryptedMessageData(
            wire,
            _serializer,
            secret,
            sendersPublicKeyBlob: keyPair.PublicKey);

        Assert.Equal(data, result);
    }

    [Fact]
    public void GetDecryptedMessageData_VerificationFails_ThrowsSecurityException()
    {
        using var keyPair = SessionKeyPairFactory.GenerateEcdsa();
        using var wrongKey = SessionKeyPairFactory.GenerateEcdsa();
        var secret = GetSharedSecret();
        var data = GetSampleData();

        var wire = _manager.CreateWireMessage(
            "Test",
            data,
            _serializer,
            keyPair: keyPair,
            sharedSecret: secret);

        Assert.Throws<SecurityException>(() =>
            _manager.GetDecryptedMessageData(
                wire,
                _serializer,
                secret,
                sendersPublicKeyBlob: wrongKey.PublicKey));
    }

    [Fact]
    public void GetDecryptedMessageData_SignedWithoutEncryption_VerifiesSignature()
    {
        using var keyPair = SessionKeyPairFactory.GenerateEcdsa();
        var data = GetSampleData();

        var wire = _manager.CreateWireMessage(
            "Test",
            data,
            _serializer,
            keyPair: keyPair,
            sharedSecret: null);

        var result = _manager.GetDecryptedMessageData(
            wire,
            _serializer,
            sharedSecret: null,
            sendersPublicKeyBlob: keyPair.PublicKey);

        Assert.Equal(data, result);
    }
}
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using CoreRemoting.RpcMessaging;
using CoreRemoting.Serialization;
using CoreRemoting.Serialization.Bson;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace CoreRemoting.Tests;

public class ClientHandshakeMessageSerializationTests
{
    private class JsonSerializerAdapter : ISerializerAdapter
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            ContractResolver = new DefaultContractResolver()
        };

        public bool EnvelopeNeededForParameterSerialization => false;

        public byte[] Serialize<T>(T graph) => Serialize(typeof(T), graph);

        public byte[] Serialize(Type type, object graph)
        {
            var json = JsonConvert.SerializeObject(graph, type, Settings);
            return Encoding.UTF8.GetBytes(json);
        }

        public T Deserialize<T>(byte[] rawData) => (T)Deserialize(typeof(T), rawData);

        public object Deserialize(Type type, byte[] rawData)
        {
            var json = Encoding.UTF8.GetString(rawData);
            return JsonConvert.DeserializeObject(json, type, Settings);
        }
    }

    private static readonly ISerializerAdapter JsonAdapter = new JsonSerializerAdapter();
    private static readonly ISerializerAdapter BsonAdapter = new BsonSerializerAdapter();

    public static IEnumerable<object[]> AllAdapters()
    {
        yield return new object[] { JsonAdapter };
        yield return new object[] { BsonAdapter };
    }

    private static T Roundtrip<T>(T obj, ISerializerAdapter adapter) =>
        adapter.Deserialize<T>(adapter.Serialize(obj));

    private static string SerializeToJsonString<T>(T obj) =>
        Encoding.UTF8.GetString(JsonAdapter.Serialize(obj));

    [Theory]
    [MemberData(nameof(AllAdapters))]
    public void EmptyObject_Roundtrip(ISerializerAdapter adapter)
    {
        var msg = new ClientHandshakeMessage();
        var restored = Roundtrip(msg, adapter);

        Assert.NotNull(restored);
        Assert.Empty(restored.Metadata);
        Assert.False(restored.MessageEncryption);
        Assert.Null(restored.ClientPublicKey);
        Assert.Null(restored.ResumableSessionId);
        Assert.Null(restored.SessionSignature);
    }

    [Theory]
    [MemberData(nameof(AllAdapters))]
    public void Roundtrip_AllProperties(ISerializerAdapter adapter)
    {
        var original = new ClientHandshakeMessage
        {
            MessageEncryption = true,
            ClientPublicKey = [0x04, 0xDE, 0xAD, 0xBE, 0xEF],
            ResumableSessionId = Guid.NewGuid(),
            SessionSignature = [0xCA, 0xFE, 0xBA, 0xBE]
        };

        var restored = Roundtrip(original, adapter);

        Assert.Equal(original.MessageEncryption, restored.MessageEncryption);
        Assert.Equal(original.ClientPublicKey, restored.ClientPublicKey);
        Assert.Equal(original.ResumableSessionId, restored.ResumableSessionId);
        Assert.Equal(original.SessionSignature, restored.SessionSignature);
    }

    [Theory]
    [MemberData(nameof(AllAdapters))]
    public void Roundtrip_ChannelMetadata(ISerializerAdapter adapter)
    {
        var original = new ClientHandshakeMessage
        {
            MessageEncryption = true,
            ClientPublicKey = [0x04, 0x01, 0x02, 0x03],
            ResumableSessionId = Guid.NewGuid(),
            SessionSignature = [0xAA, 0xBB]
        };

        original.SetValue("ChannelName", "quic");
        original.SetValue("ChannelPort", 443);
        original.SetValue("RemoteAddress", IPAddress.Parse("10.0.0.1"));
        original.SetValue("ConnectedAt", DateTime.UtcNow);

        var restored = Roundtrip(original, adapter);

        Assert.Equal(original.MessageEncryption, restored.MessageEncryption);
        Assert.Equal(original.ClientPublicKey, restored.ClientPublicKey);
        Assert.Equal(original.ResumableSessionId, restored.ResumableSessionId);
        Assert.Equal(original.SessionSignature, restored.SessionSignature);

        Assert.Equal("quic", restored.GetValue<string>("ChannelName"));
        Assert.Equal(443, restored.GetValue<int>("ChannelPort"));
        Assert.Equal(IPAddress.Parse("10.0.0.1"), restored.GetValue<IPAddress>("RemoteAddress"));
        Assert.Equal(original.GetValue<DateTime>("ConnectedAt"), restored.GetValue<DateTime>("ConnectedAt"));
    }

    [Fact]
    public void Json_ContainsOnlyMetadata()
    {
        var msg = new ClientHandshakeMessage
        {
            MessageEncryption = true,
            ClientPublicKey = [1, 2, 3],
            ResumableSessionId = Guid.NewGuid()
        };

        var json = SerializeToJsonString(msg);

        Assert.Contains("\"Metadata\"", json);
        Assert.Contains("MessageEncryption", json);
        Assert.Contains("ClientPublicKey", json);
        Assert.Contains("\"ResumableSessionId\"", json);
        Assert.DoesNotContain("\"SessionSignature\":", json);
    }

    [Theory]
    [MemberData(nameof(AllAdapters))]
    public void Roundtrip_SpecificFormats(ISerializerAdapter adapter)
    {
        var guid = Guid.NewGuid();
        var msg1 = new ClientHandshakeMessage { ResumableSessionId = guid };
        Assert.Equal(guid, Roundtrip(msg1, adapter).ResumableSessionId);

        var key = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var msg2 = new ClientHandshakeMessage { ClientPublicKey = key };
        Assert.Equal(key, Roundtrip(msg2, adapter).ClientPublicKey);

        var msg3 = new ClientHandshakeMessage { MessageEncryption = true };
        Assert.True(Roundtrip(msg3, adapter).MessageEncryption);

        var now = DateTime.UtcNow;
        var msg4 = new ClientHandshakeMessage();
        msg4.SetValue("Timestamp", now);
        var restored4 = Roundtrip(msg4, adapter).GetValue<DateTime>("Timestamp");
        Assert.Equal(now, restored4);
        Assert.Equal(DateTimeKind.Utc, restored4.Kind);
    }

    [Fact]
    public void Json_SpecificFormats_TextRepresentation()
    {
        var guid = Guid.NewGuid();
        var msg1 = new ClientHandshakeMessage { ResumableSessionId = guid };
        Assert.Contains(guid.ToString("D"), SerializeToJsonString(msg1));

        var key = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var msg2 = new ClientHandshakeMessage { ClientPublicKey = key };
        Assert.Contains(Convert.ToBase64String(key), SerializeToJsonString(msg2));

        var msg3 = new ClientHandshakeMessage { MessageEncryption = true };
        Assert.Contains("\"1\"", SerializeToJsonString(msg3));
    }

    [Theory]
    [InlineData("{\"Metadata\":{\"MessageEncryption\":\"1\",\"ClientPublicKey\":\"AQID\"}}", true, new byte[] { 1, 2, 3 })]
    [InlineData("{\"Metadata\":{\"MessageEncryption\":\"True\",\"ClientPublicKey\":\"BAUG\"}}", true, new byte[] { 4, 5, 6 })]
    [InlineData("{\"Metadata\":{\"MessageEncryption\":\"false\",\"ClientPublicKey\":\"\"}}", false, null)]
    public void Json_DeserializeFromManualJson(string json, bool expectedEncryption, byte[] expectedKey)
    {
        var restored = JsonAdapter.Deserialize<ClientHandshakeMessage>(Encoding.UTF8.GetBytes(json));

        Assert.Equal(expectedEncryption, restored.MessageEncryption);
        Assert.Equal(expectedKey ?? [], restored.ClientPublicKey ?? []);
        Assert.Null(restored.ResumableSessionId);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("True", true)]
    [InlineData("true", true)]
    [InlineData("False", false)]
    [InlineData("false", false)]
    public void GetValue_Bool_ParsesVariousFormats(string value, bool expected)
    {
        var msg = new ClientHandshakeMessage();
        msg.Metadata["TestBool"] = value;

        var result = msg.GetValue<bool>("TestBool");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SetValue_Null_RemovesKey()
    {
        var msg = new ClientHandshakeMessage();
        msg.SetValue("Key", "value");
        Assert.Contains("Key", msg.Metadata);

        msg.SetValue<string>("Key", null);
        Assert.DoesNotContain("Key", msg.Metadata);
    }

    [Fact]
    public void GetValue_MissingKey_ReturnsDefault()
    {
        var msg = new ClientHandshakeMessage();
        Assert.Equal(42, msg.GetValue("Missing", 42));
        Assert.Null(msg.GetValue<string>("Missing", null));
        Assert.False(msg.GetValue("MissingBool", false));
    }

    [Fact]
    public void GetValue_InvalidBase64_ReturnsDefault()
    {
        var msg = new ClientHandshakeMessage();
        msg.Metadata["BadBytes"] = "not-base64!";

        var result = msg.GetValue<byte[]>("BadBytes");
        Assert.Null(result);
    }

    [Fact]
    public void GetValue_InvalidBool_ReturnsDefault()
    {
        var msg = new ClientHandshakeMessage();
        msg.Metadata["BadBool"] = "invalid";

        var result = msg.GetValue("BadBool", true);
        Assert.True(result);
    }

    [Fact]
    public void Json_ContainsOnlySetMetadataKeys()
    {
        var msg = new ClientHandshakeMessage();
        msg.MessageEncryption = true;
        msg.ClientPublicKey = [1, 2, 3];

        var json = SerializeToJsonString(msg);
        var restored = JsonAdapter.Deserialize<ClientHandshakeMessage>(Encoding.UTF8.GetBytes(json));

        Assert.Equal(2, restored.Metadata.Count);
        Assert.Contains("MessageEncryption", restored.Metadata);
        Assert.Contains("ClientPublicKey", restored.Metadata);
        Assert.DoesNotContain("ResumableSessionId", restored.Metadata);
        Assert.DoesNotContain("SessionSignature", restored.Metadata);
    }
}
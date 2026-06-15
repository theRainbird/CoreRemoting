using System;
using System.Collections;
using CoreRemoting.Serialization.Bson;
using Xunit;
using CoreRemoting.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace CoreRemoting.Tests
{
    public class BsonHashtableRpcTests
    {
        [Fact]
        public void BsonSerializerAdapter_should_handle_Hashtable_with_objects_in_RPC_scenario()
        {
            // Simulate RPC scenario where Hashtable parameters are wrapped in Envelope
            var originalHashtable = new Hashtable();
            originalHashtable["StringValue"] = "test value";
            originalHashtable["IntValue"] = 42;
            originalHashtable["NestedHashtable"] = new Hashtable { ["inner"] = "value" };

            var serializer = new BsonSerializerAdapter();

            // Simulate what happens in RPC: parameter is wrapped in Envelope
            var envelope = new Envelope(originalHashtable);
            var serializedBytes = serializer.Serialize(envelope);

            // Deserialize back to Envelope (as in RPC)
            var deserializedEnvelope = serializer.Deserialize<Envelope>(serializedBytes);
            var deserializedHashtable = (Hashtable)deserializedEnvelope.Value;

            // Verify all values are correctly deserialized with proper types
            Assert.Equal(originalHashtable.Count, deserializedHashtable.Count);

            // Check string value
            Assert.Equal("test value", deserializedHashtable["StringValue"]);
            Assert.True(deserializedHashtable["StringValue"] is string);

            // Check integer value
            Assert.Equal(42, deserializedHashtable["IntValue"]);
            Assert.True(deserializedHashtable["IntValue"] is int);

            // Check nested hashtable
            var originalNested = (Hashtable)originalHashtable["NestedHashtable"];
            var deserializedNested = (Hashtable)deserializedHashtable["NestedHashtable"];
            Assert.Equal(originalNested["inner"], deserializedNested["inner"]);
        }

        [Fact]
        public void BsonSerializerAdapter_should_preserve_Hashtable_types_without_Envelope()
        {
            // Test normal Hashtable serialization (not in RPC scenario)
            var originalHashtable = new Hashtable();
            originalHashtable["StringValue"] = "test value";
            originalHashtable["IntValue"] = 42;

            var serializer = new BsonSerializerAdapter();
            var serializedBytes = serializer.Serialize(originalHashtable);
            var deserializedHashtable = serializer.Deserialize<Hashtable>(serializedBytes);

            // Verify all values are correctly deserialized with proper types
            Assert.Equal(originalHashtable.Count, deserializedHashtable.Count);
            Assert.Equal("test value", deserializedHashtable["StringValue"]);
            Assert.True(deserializedHashtable["StringValue"] is string);
            Assert.Equal(42, deserializedHashtable["IntValue"]);
            Assert.True(deserializedHashtable["IntValue"] is int);
        }
    }

    #region Hashtable Deserialization - RPC Roundtrip Test

    public interface ITestService
    {
        Hashtable Echo(Hashtable input);
    }

    public class TestService : ITestService
    {
        public Hashtable Echo(Hashtable input)
        {
            if (input != null)
            {
                foreach (DictionaryEntry entry in input)
                {
                    if (entry.Value is JObject)
                    {
                        throw new InvalidOperationException(
                            $"BUG REPRODUCED: Parameter '{entry.Key}' deserialized as JObject. " +
                            $"Original type was lost during deserialization.");
                    }
                }
            }

            return input;
        }
    }

    public class HashtableDeserializationBugTest : IDisposable
    {
        private readonly RemotingServer _server;
        private readonly RemotingClient _client;

        public HashtableDeserializationBugTest()
        {
            var serverConfig = new ServerConfig
            {
                HostName = "localhost",
                NetworkPort = 9099,
                RegisterServicesAction = container =>
                {
                    container.RegisterService<ITestService, TestService>(
                        lifetime: ServiceLifetime.Singleton);
                }
            };

            _server = new RemotingServer(serverConfig);
            _server.Start();

            var clientConfig = new ClientConfig
            {
                ServerHostName = "localhost",
                ServerPort = 9099,
                ConnectionTimeout = 5000
            };

            _client = new RemotingClient(clientConfig);
            _client.Connect();
        }

        [Fact]
        public void Hashtable_ShouldSurviveRoundtrip_WithoutBecomingJObject()
        {
            var proxy = _client.CreateProxy<ITestService>();
            var original = new Hashtable
            {
                { "@param1", "test_value" },
                { "@param2", 123 },
                { "@param3", true }
            };

            var exception = Record.Exception(() =>
            {
                var result = proxy.Echo(original);

                foreach (DictionaryEntry entry in result)
                {
                    Assert.IsNotType<JObject>(entry.Value);
                }
            });

            Assert.Null(exception);
        }

        public void Dispose()
        {
            _client?.Dispose();
            _server?.Dispose();
        }
    }

    #endregion
}

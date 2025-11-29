using System;
using System.Collections;
using CoreRemoting.Serialization.Bson;
using Xunit;

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
}
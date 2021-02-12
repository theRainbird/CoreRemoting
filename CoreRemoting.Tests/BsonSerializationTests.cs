using System;
using CoreRemoting.RpcMessaging;
using CoreRemoting.Serialization.Bson;
using CoreRemoting.Tests.Tools;
using NUnit.Framework;

namespace CoreRemoting.Tests
{
    public class BsonSerializationTests
    {
        [Test]
        public void BsonSerializerAdapter_should_deserialize_MethodCallMessage()
        {
            var serializer = new BsonSerializerAdapter();
            var testServiceInterfaceType = typeof(ITestService);
            
            var messageBuilder = new MethodCallMessageBuilder();

            var message =
                messageBuilder.BuildMethodCallMessage(
                    serializer,
                    testServiceInterfaceType.Name,
                    testServiceInterfaceType.GetMethod("TestMethod"),
                    new object[] { 4711});

            var rawData = serializer.Serialize(message);
            
            var deserializedMessage = serializer.Deserialize<MethodCallMessage>(rawData);
            
            deserializedMessage.UnwrapParametersFromDeserializedMethodCallMessage(
                out var parameterValues,
                out var parameterTypes);
            
            Assert.AreEqual(1, deserializedMessage.Parameters.Length);
            Assert.NotNull(deserializedMessage.Parameters[0]);
            Assert.AreEqual("arg", deserializedMessage.Parameters[0].ParameterName);
            Assert.AreEqual("System.Object", deserializedMessage.Parameters[0].ParameterTypeName);
            Assert.AreEqual(typeof(int), parameterValues[0].GetType());
            Assert.AreEqual(typeof(object), parameterTypes[0]);
            Assert.AreEqual(4711, parameterValues[0]);
        }

        public void BsonSerializerAdapter_should_deserialize_CompleteHandshakeWireMessage()
        {
            var sessionId = Guid.NewGuid();
            
            var completeHandshakeMessage =
                new WireMessage
                {
                    MessageType = "complete_handshake",
                    Data = sessionId.ToByteArray()
                };   
            
            var serializer = new BsonSerializerAdapter();
            var rawData = serializer.Serialize(completeHandshakeMessage);

            var deserializedMessage = serializer.Deserialize<WireMessage>(rawData);
            
            Assert.AreEqual("complete_handshake", deserializedMessage.MessageType);
            Assert.AreEqual(sessionId, new Guid(deserializedMessage.Data));
        }
    }
}
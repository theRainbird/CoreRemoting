using System;
using CoreRemoting.RpcMessaging;
using CoreRemoting.Serialization.Binary;
using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests
{
    public class BinarySerializationTests
    {
        [Fact]
        public void BinarySerializerAdapter_should_deserialize_MethodCallMessage()
        {
            var serializer = new BinarySerializerAdapter();
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

            var parametersLength = deserializedMessage.Parameters.Length;
            
            Assert.Equal(1, parametersLength);
            Assert.NotNull(deserializedMessage.Parameters[0]);
            Assert.Equal("arg", deserializedMessage.Parameters[0].ParameterName);
            Assert.StartsWith("System.Object,", deserializedMessage.Parameters[0].ParameterTypeName);
            Assert.Equal(typeof(int), parameterValues[0].GetType());
            Assert.Equal(typeof(object), parameterTypes[0]);
            Assert.Equal(4711, parameterValues[0]);
        }

        [Fact]
        public void BinarySerializerAdapter_should_deserialize_CompleteHandshakeWireMessage()
        {
            var sessionId = Guid.NewGuid();
            
            var completeHandshakeMessage =
                new WireMessage
                {
                    MessageType = "complete_handshake",
                    Data = sessionId.ToByteArray()
                };   
            
            var serializer = new BinarySerializerAdapter();
            var rawData = serializer.Serialize(completeHandshakeMessage);

            var deserializedMessage = serializer.Deserialize<WireMessage>(rawData);
            
            Assert.Equal("complete_handshake", deserializedMessage.MessageType);
            Assert.Equal(sessionId, new Guid(deserializedMessage.Data));
        }
    }
}
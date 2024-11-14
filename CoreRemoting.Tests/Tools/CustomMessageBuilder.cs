using System;
using System.Collections.Generic;
using System.Reflection;
using CoreRemoting.RpcMessaging;
using CoreRemoting.Serialization;

namespace CoreRemoting.Tests.Tools
{
    /// <summary>
    /// Custom client-side RPC message processor.
    /// </summary>
    public class CustomMessageBuilder : IMethodCallMessageBuilder
    {
        public CustomMessageBuilder()
        {
            Builder = new MethodCallMessageBuilder();
        }

        public Action<MethodCallMessage> ProcessMethodCallMessage { get; set; } = m => { };

        public Action<IEnumerable<MethodCallParameterMessage>> ProcessMethodParameterInfos { get; set; } = m => { };

        public Action<MethodCallResultMessage> ProcessMethodCallResultMessage { get; set; } = m => { };

        private MethodCallMessageBuilder Builder { get; set; }

        public MethodCallMessage BuildMethodCallMessage(ISerializerAdapter serializer, string remoteServiceName, MethodInfo targetMethod, object[] args)
        {
            var m = Builder.BuildMethodCallMessage(serializer, remoteServiceName, targetMethod, args);
            ProcessMethodCallMessage(m);
            return m;
        }

        public IEnumerable<MethodCallParameterMessage> BuildMethodParameterInfos(ISerializerAdapter serializer, MethodInfo targetMethod, object[] args)
        {
            var m = Builder.BuildMethodParameterInfos(serializer, targetMethod, args);
            ProcessMethodParameterInfos(m);
            return m;
        }

        public MethodCallResultMessage BuildMethodCallResultMessage(ISerializerAdapter serializer, Guid uniqueCallKey, MethodInfo method, object[] args, object returnValue)
        {
            var m = Builder.BuildMethodCallResultMessage(serializer, uniqueCallKey, method, args, returnValue);
            ProcessMethodCallResultMessage(m);
            return m;
        }
    }
}

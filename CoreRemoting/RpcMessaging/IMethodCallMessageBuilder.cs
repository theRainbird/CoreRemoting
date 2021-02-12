using System;
using System.Collections.Generic;
using System.Reflection;
using CoreRemoting.Serialization;

namespace CoreRemoting.RpcMessaging
{
    /// <summary>
    /// Interface for message builder component.
    /// </summary>
    public interface IMethodCallMessageBuilder
    {
        /// <summary>
        /// Builds a new method call message.
        /// </summary>
        /// <param name="serializer">Serializer adapter used to serialize argument values</param>
        /// <param name="remoteServiceName">Unique name of the remote service that should be called</param>
        /// <param name="targetMethod">Target method information</param>
        /// <param name="args">Array of arguments, which should passed a parameters</param>
        /// <returns>The created method call message</returns>
        MethodCallMessage BuildMethodCallMessage(
            ISerializerAdapter serializer,
            string remoteServiceName,
            MethodInfo targetMethod,
            object[] args);

        /// <summary>
        /// Builds method call parameter messages from arguments for a specified target method.
        /// </summary>
        /// <param name="serializer">Serializer adapter used to serialize argument values</param>
        /// <param name="targetMethod">Target method information</param>
        /// <param name="args">Array of arguments, which should passed a parameters</param>
        /// <returns>Enumerable of method call parameter messages</returns>
        IEnumerable<MethodCallParameterMessage> BuildMethodParameterInfos(
            ISerializerAdapter serializer,
            MethodInfo targetMethod,
            object[] args);

        /// <summary>
        /// Builds a new method call result message.
        /// </summary>
        /// <param name="serializer">Serializer adapter used to serialize argument values</param>
        /// <param name="uniqueCallKey">Unique key to correlate RPC call</param>
        /// <param name="method">Method information of the called method</param>
        /// <param name="args">Arguments</param>
        /// <param name="returnValue">Returned return value</param>
        /// <returns>Method call result message</returns>
        MethodCallResultMessage BuildMethodCallResultMessage(
            ISerializerAdapter serializer,
            Guid uniqueCallKey,
            MethodInfo method,
            object[] args,
            object returnValue);
    }
}
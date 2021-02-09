using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoreRemoting.Serialization;

namespace CoreRemoting.RpcMessaging
{
    /// <summary>
    /// Method call message builder component.
    /// </summary>
    public class MethodCallMessageBuilder : IMethodCallMessageBuilder
    {
        /// <summary>
        /// Builds a new method call message.
        /// </summary>
        /// <param name="serializer">Serializer adapter used to serialize argument values</param>
        /// <param name="remoteServiceName">Unique name of the remote service that should be called</param>
        /// <param name="targetMethod">Target method information</param>
        /// <param name="args">Array of arguments, which should passed a parameters</param>
        /// <param name="knownTypes">Optional list of known types for safe deserialization (only needed if the configured serializer needs known types)</param>
        /// <returns>The created method call message</returns>
        public MethodCallMessage BuildMethodCallMessage(
            ISerializerAdapter serializer,
            string remoteServiceName, 
            MethodInfo targetMethod, 
            object[] args,
            List<Type> knownTypes = null)
        {
            if (targetMethod == null)
                throw new ArgumentNullException(nameof(targetMethod));

            if (serializer == null)
                throw new ArgumentNullException(nameof(serializer));
            
            args ??= new object[0];

            var message = new MethodCallMessage()
            {
                ServiceName = remoteServiceName,
                MethodName = targetMethod.Name,
                Parameters = BuildMethodParameterInfos(serializer, targetMethod, args).ToArray(),
                CallContextSnapshot = CallContext.GetSnapshot()
            };

            return message;
        }

        /// <summary>
        /// Builds method call parameter messages from arguments for a specified target method.
        /// </summary>
        /// <param name="serializer">Serializer adapter used to serialize argument values</param>
        /// <param name="targetMethod">Target method information</param>
        /// <param name="args">Array of arguments, which should passed a parameters</param>
        /// <param name="knownTypes">Optional list of known types for safe deserialization (only needed if the configured serializer needs known types)</param>
        /// <returns>Enumerable of method call parameter messages</returns>
        public IEnumerable<MethodCallParameterMessage> BuildMethodParameterInfos(
            ISerializerAdapter serializer, 
            MethodInfo targetMethod, 
            object[] args,
            List<Type> knownTypes = null)
        {
            var parameterInfos = targetMethod.GetParameters();

            for (var i = 0; i < parameterInfos.Length; i++)
            {
                var arg = args[i];
                var parameterInfo = parameterInfos[i];

                var useParamArray =
                    args.Length > parameterInfos.Length &&
                    i == parameterInfos.Length -1 &&
                    parameterInfos[i].GetCustomAttribute<ParamArrayAttribute>() != null;
                
                var paramArrayValues = new List<object>();
                
                if (useParamArray)
                {
                    for (var j = i; j < args.Length; j++)
                    {
                        paramArrayValues.Add(args[j]);
                    }
                }

                var isArgNull = arg == null;

                var parameterValue = 
                    useParamArray 
                        ? paramArrayValues.ToArray() 
                        : arg;

                var parameterValueRawData =
                    serializer.Serialize(parameterValue, knownTypes);
                
                yield return
                    new MethodCallParameterMessage()
                    {
                        IsOut = parameterInfo.IsOut,
                        ParameterName = parameterInfo.Name,
                        ParameterTypeName = parameterInfo.ParameterType.FullName,
                        Value = parameterValueRawData,
                        IsValueNull = isArgNull
                    };
            }
        }
        
        /// <summary>
        /// Builds a new method call result message.
        /// </summary>
        /// <param name="serializer">Serializer adapter used to serialize argument values</param>
        /// <param name="uniqueCallKey">Unique key to correlate RPC call</param>
        /// <param name="method">Method information of the called method</param>
        /// <param name="args">Arguments</param>
        /// <param name="returnValue">Returned return value</param>
        /// <param name="knownTypes">Optional list of known types for safe deserialization (only needed if the configured serializer needs known types)</param>
        /// <returns>Method call result message</returns>
        public MethodCallResultMessage BuildMethodCallResultMessage(
            ISerializerAdapter serializer,
            Guid uniqueCallKey, 
            MethodInfo method, 
            object[] args, 
            object returnValue,
            List<Type> knownTypes = null)
        {
            if (serializer == null)
                throw new ArgumentNullException(nameof(serializer));
            
            var isReturnValueNull = returnValue == null;
            
            var parameterInfos = method.GetParameters();

            var message = new MethodCallResultMessage()
            { 
                IsReturnValueNull = isReturnValueNull,
                ReturnValue = returnValue
            };

            var outParameters = new List<MethodCallOutParameterMessage>();
            
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                var parameterInfo = parameterInfos[i];
                
                if (!parameterInfo.IsOut)
                    continue;
                
                var isArgNull = arg == null;

                var serializedArgValue = serializer.Serialize(parameterInfo.ParameterType, arg, knownTypes);
                
                outParameters.Add(
                    new MethodCallOutParameterMessage()
                    {
                        ParameterName = parameterInfo.Name,
                        OutValue = serializedArgValue,
                        IsOutValueNull = isArgNull
                    });
            }

            message.OutParameters = outParameters.ToArray();
            message.CallContextSnapshot = CallContext.GetSnapshot();
            
            return message;
        }
    }
}
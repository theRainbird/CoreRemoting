using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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
        /// <param name="remoteServiceName">Unique name of the remote service that should be called</param>
        /// <param name="targetMethod">Target method information</param>
        /// <param name="args">Array of arguments, which should passed a parameters</param>
        /// <returns>The created method call message</returns>
        public MethodCallMessage BuildMethodCallMessage(string remoteServiceName, MethodInfo targetMethod, object[] args)
        {
            if (targetMethod == null)
                throw new ArgumentNullException(nameof(targetMethod));

            args ??= new object[0];

            var message = new MethodCallMessage()
            {
                ServiceName = remoteServiceName,
                MethodName = targetMethod.Name,
                Parameters = BuildMethodParameterInfos(targetMethod, args).ToArray(),
                CallContextSnapshot = CallContext.GetSnapshot()
            };

            return message;
        }

        /// <summary>
        /// Builds method call parameter messages from arguments for a specified target method.
        /// </summary>
        /// <param name="targetMethod">Target method information</param>
        /// <param name="args">Array of arguments, which should passed a parameters</param>
        /// <returns>Enumerable of method call parameter messages</returns>
        public IEnumerable<MethodCallParameterMessage> BuildMethodParameterInfos(MethodInfo targetMethod, object[] args)
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
                
                yield return
                    new MethodCallParameterMessage()
                    {
                        IsOut = parameterInfo.IsOut,
                        ParameterName = parameterInfo.Name,
                        ParameterTypeName = parameterInfo.ParameterType.FullName,
                        Value = parameterValue,
                        IsValueNull = isArgNull
                    };
            }
        }
        
        /// <summary>
        /// Builds a new method call result message.
        /// </summary>
        /// <param name="uniqueCallKey">Unique key to correlate RPC call</param>
        /// <param name="method">Method information of the called method</param>
        /// <param name="args">Arguments</param>
        /// <param name="returnValue">Returned return value</param>
        /// <returns>Method call result message</returns>
        public MethodCallResultMessage BuildMethodCallResultMessage(Guid uniqueCallKey, MethodInfo method, object[] args, object returnValue)
        {
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
                
                outParameters.Add(
                    new MethodCallOutParameterMessage()
                    {
                        ParameterName = parameterInfo.Name,
                        OutValue = arg,
                        IsOutValueNull = isArgNull
                    });
            }

            message.OutParameters = outParameters.ToArray();
            message.CallContextSnapshot = CallContext.GetSnapshot();
            
            return message;
        }
    }
}
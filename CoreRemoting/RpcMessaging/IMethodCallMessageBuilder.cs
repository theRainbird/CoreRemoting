using System;
using System.Collections.Generic;
using System.Reflection;

namespace CoreRemoting.RpcMessaging
{
    public interface IMethodCallMessageBuilder
    {
        MethodCallMessage BuildMethodCallMessage(string remoteServiceName, MethodInfo targetMethod, object[] args);
        IEnumerable<MethodCallParameterMessage> BuildMethodParameterInfos(MethodInfo targetMethod, object[] args);
        MethodCallResultMessage BuildMethodCallResultMessage(Guid uniqueCallKey, MethodInfo method, object[] args, object returnValue);
    }
}
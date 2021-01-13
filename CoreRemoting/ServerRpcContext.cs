using System;
using System.Diagnostics.CodeAnalysis;
using CoreRemoting.RpcMessaging;

namespace CoreRemoting
{
    public class ServerRpcContext
    {
        public Guid UniqueCallKey { get; set; }
        
        public Exception Exception { get; set; }
        
        public MethodCallMessage MethodCallMessage { get; internal set; }
        
        public MethodCallResultMessage MethodCallResultMessage { get; set; }
        
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")] 
        public object ServiceInstance { get; set; }
        
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
        public RemotingSession Session { get; set; }
    }
}
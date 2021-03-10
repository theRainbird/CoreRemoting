using System;
using System.Diagnostics.CodeAnalysis;
using CoreRemoting.RpcMessaging;

namespace CoreRemoting
{
    /// <summary>
    /// Describes the server side context of a RPC call.
    /// </summary>
    public class ServerRpcContext
    {
        /// <summary>
        /// Gets or sets the unique key of RPC call.
        /// </summary>
        public Guid UniqueCallKey { get; set; }
        
        /// <summary>
        /// Gets or sets the last exception that is occurred.
        /// </summary>
        public Exception Exception { get; set; }
        
        /// <summary>
        /// Gets the message that describes the remote method call.
        /// </summary>
        public MethodCallMessage MethodCallMessage { get; internal set; }
        
        /// <summary>
        /// Gets or sets the message that contains the results of a remote method call.
        /// </summary>
        public MethodCallResultMessage MethodCallResultMessage { get; set; }
        
        /// <summary>
        /// Gets or sets the instance of the service, on which the method is called.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")] 
        public object ServiceInstance { get; set; }
        
        /// <summary>
        /// Gets or sets the CoreRemoting session that is used to handle the RPC.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
        public RemotingSession Session { get; set; }
    }
}
using System;
using System.Threading;
using CoreRemoting.RpcMessaging;

namespace CoreRemoting
{
    /// <summary>
    /// Describes the client side context of a RPC call.
    /// </summary>
    public class ClientRpcContext : IDisposable
    {
        /// <summary>
        /// Creates a new instance of the ClientRpcContext class.
        /// </summary>
        internal ClientRpcContext()
        {
            UniqueCallKey = Guid.NewGuid();
            WaitHandle = new EventWaitHandle(initialState: false, EventResetMode.ManualReset);
        }
        
        /// <summary>
        /// Gets the unique key of RPC call.
        /// </summary>
        public Guid UniqueCallKey { get; }
        
        /// <summary>
        /// Gets or sets the result message, that was received from server after the call was invoked on server side.
        /// </summary>
        public MethodCallResultMessage ResultMessage { get; set; }
        
        /// <summary>
        /// Gets or sets whether this RPC call is in error state.
        /// </summary>
        public bool Error { get; set; }

        /// <summary>
        /// Gets or sets an exception that describes an error that occurred on server side RPC invocation.
        /// </summary>
        public RemoteInvocationException RemoteException { get; set; }
        
        /// <summary>
        /// Gets a wait handle that is set, when the response of this RPC call is received from server.
        /// </summary>
        public EventWaitHandle WaitHandle { get; }
        
        /// <summary>
        /// Frees managed resources.
        /// </summary>
        public void Dispose()
        {
            WaitHandle?.Dispose();
        }
    }
}
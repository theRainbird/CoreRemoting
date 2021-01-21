using System;

namespace CoreRemoting.RemoteDelegates
{
    /// <summary>
    /// Event aggregator to fire event if remote delegate is invoked.
    /// </summary>
    internal class RemoteDelegateInvocationEventAggregator
    {
        /// <summary>
        /// Creates a new instance of the RemoteDelegateInvocationEventAggregator class.
        /// </summary>
        /// <param name="delgateType">Delegate type</param>
        /// <param name="uniqueCallKey">Unique key to correlate the RPC call</param>
        /// <param name="handlerKey">Unique handler key to correlate the client delegate to be called, when remote delegate is invoked</param>
        /// <param name="remoteDelegateArguments">Arguments of remote delegate invocation</param>
        internal delegate object RemoteDelegateInvocationNeededEventHandler(
            Type delgateType,
            Guid uniqueCallKey,
            Guid handlerKey,
            object[] remoteDelegateArguments);
        
        /// <summary>
        /// Event: Fired on client side, when a remote delegate is invoked.
        /// </summary>
        internal event RemoteDelegateInvocationNeededEventHandler RemoteDelegateInvocationNeeded;
        
        /// <summary>
        /// To be called on server side to invoke a remote delegate, which will callback the correlating client delegate.
        /// </summary>
        /// <param name="delegateType">Delegate type</param>
        /// <param name="handlerKey">Unique handle key of the client delegate</param>
        /// <param name="remoteDelegateArguments">Arguments of remote delegate invocation</param>
        /// <returns>Return value provided by the client side callback</returns>
        internal object InvokeRemoteDelegate(Type delegateType, Guid handlerKey, object[] remoteDelegateArguments)
        {
            return
                RemoteDelegateInvocationNeeded?.Invoke(
                    delgateType: delegateType,
                    uniqueCallKey: Guid.NewGuid(),
                    handlerKey: handlerKey, 
                    remoteDelegateArguments: remoteDelegateArguments);
        }
    }
}
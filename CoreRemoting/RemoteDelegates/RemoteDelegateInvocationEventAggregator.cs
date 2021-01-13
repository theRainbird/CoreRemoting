using System;

namespace CoreRemoting.RemoteDelegates
{
    internal class RemoteDelegateInvocationEventAggregator
    {
        internal delegate object RemoteDelegateInvocationNeededEventHandler(
            Type delgateType,
            Guid uniqueCallKey,
            Guid handlerKey,
            object[] remoteDelegateArguments);
        internal event RemoteDelegateInvocationNeededEventHandler RemoteDelegateInvocationNeeded;
        
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
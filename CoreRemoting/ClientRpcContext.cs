using System;
using System.Collections.Generic;
using System.Threading;
using CoreRemoting.RpcMessaging;

namespace CoreRemoting
{
    public class ClientRpcContext : IDisposable
    {
        internal ClientRpcContext()
        {
            UniqueCallKey = Guid.NewGuid();
            WaitHandle = new EventWaitHandle(initialState: false, EventResetMode.ManualReset);
        }
        
        public Guid UniqueCallKey { get; }
        
        public MethodCallResultMessage ResultMessage { get; set; }
        
        public bool Error { get; set; }

        public RemoteInvocationException RemoteException { get; set; }
        
        public IEnumerable<Type> KnownTypes { get; set; }
        
        public EventWaitHandle WaitHandle { get; }
        
        public void Dispose()
        {
            WaitHandle?.Dispose();
        }
    }
}
using System;

namespace CoreRemoting.RemoteDelegates
{
    public class ClientDelegateInfo
    {
        public ClientDelegateInfo(Delegate clientDelegate, object serviceProxy)
        {
            ClientDelegate = clientDelegate;
            ServiceProxy = serviceProxy;
        }
        
        public Delegate ClientDelegate { get; }
        
        public object ServiceProxy { get; }
    }
}
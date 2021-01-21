using System;

namespace CoreRemoting.RemoteDelegates
{
    /// <summary>
    /// Paires a client delegate with a service proxy instance.
    /// </summary>
    public class ClientDelegateInfo
    {
        public ClientDelegateInfo(Delegate clientDelegate, object serviceProxy)
        {
            ClientDelegate = clientDelegate;
            ServiceProxy = serviceProxy;
        }
        
        /// <summary>
        /// Gets the client delegate.
        /// </summary>
        public Delegate ClientDelegate { get; }
        
        /// <summary>
        /// Gets the service proxy.
        /// </summary>
        public object ServiceProxy { get; }
    }
}
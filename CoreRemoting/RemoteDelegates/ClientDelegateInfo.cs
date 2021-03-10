using System;

namespace CoreRemoting.RemoteDelegates
{
    /// <summary>
    /// Pairs a client delegate with a service proxy instance.
    /// </summary>
    public class ClientDelegateInfo
    {
        /// <summary>
        /// Creates a new instance of the ClientDelegateInfo class.
        /// </summary>
        /// <param name="clientDelegate">Client delegate</param>
        /// <param name="serviceProxy">Proxy object of the remote service, which invokes the client delegate as callback</param>
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
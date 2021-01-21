using System;

namespace CoreRemoting.RemoteDelegates
{
    /// <summary>
    /// Factory component to create delegate proxy instances.
    /// </summary>
    public class DelegateProxyFactory : IDelegateProxyFactory
    {
        /// <summary>
        /// Creates a proxy for the specified delegate type.
        /// </summary>
        /// <param name="delegateType">Delegate type to be proxied</param>
        /// <param name="callInterceptionHandler">Function to be called when intercepting calls on the delegate</param>
        /// <returns>Delegate proxy</returns>
        public IDelegateProxy Create(Type delegateType, Func<object[], object> callInterceptionHandler)
        {
            return new DelegateProxy(delegateType, callInterceptionHandler);
        }
    }
}
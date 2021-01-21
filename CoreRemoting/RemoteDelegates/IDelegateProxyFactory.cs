using System;

namespace CoreRemoting.RemoteDelegates
{
    /// <summary>
    /// Interface to be implemented by delegate proxy factory components.
    /// </summary>
    public interface IDelegateProxyFactory
    {
        /// <summary>
        /// Creates a proxy for the specified delegate type.
        /// </summary>
        /// <param name="delegateType">Delegate type to be proxied</param>
        /// <param name="callInterceptionHandler">Function to be called when intercepting calls on the delegate</param>
        /// <returns>Delegate proxy</returns>
        IDelegateProxy Create(Type delegateType, Func<object[], object> callInterceptionHandler);
    }
}
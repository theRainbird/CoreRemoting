using System;

namespace CoreRemoting.RemoteDelegates
{
    public interface IDelegateProxy : IDisposable
    {
        /// <summary>
        /// Gets the proxied delegate.
        /// </summary>
        Delegate ProxiedDelegate { get; }
    }
}
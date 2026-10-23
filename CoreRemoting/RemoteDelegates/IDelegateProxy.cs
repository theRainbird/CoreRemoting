using System;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Interface to be implemented by delegate proxies.
/// </summary>
public interface IDelegateProxy : IDisposable
{
    /// <summary>
    /// Gets the proxied delegate.
    /// </summary>
    Delegate ProxiedDelegate { get; }

    /// <summary>
    /// Gets whether this proxy has been disposed. Once disposed, invoking
    /// the proxied delegate is a no-op (returns the default value for the
    /// delegate's return type).
    /// </summary>
    bool IsDisposed { get; }
}

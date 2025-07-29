using System;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Delegate invoker interface.
/// </summary>
public interface IDelegateInvoker
{
    /// <summary>
    /// Invokes the given delegate dynamically.
    /// </summary>
    /// <param name="handler">Delegate instance.</param>
    /// <param name="arguments">Arguments</param>
    void Invoke(Delegate handler, object[] arguments);
}

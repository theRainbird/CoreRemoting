using System;
using System.Reflection.Emit;
using System.Threading;
using CoreRemoting.Toolbox;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Proxy for intercepting calls on a specified delegate type.
/// </summary>
public sealed class DelegateProxy : IDelegateProxy
{
    private Func<object[], object> _callInterceptionHandler;
    private readonly object _disposedReturnValue;

    /// <summary>
    /// Creates a new instance of the DelegateProxy class.
    /// </summary>
    /// <param name="delegateType">Delegate type to be proxied</param>
    /// <param name="callInterceptionHandler">Function to be called when intercepting calls on the delegate</param>
    /// <param name="dynamicMethod">Dynamic method that provides the delegate invocation body</param>
    internal DelegateProxy(Type delegateType, Func<object[], object> callInterceptionHandler, DynamicMethod dynamicMethod)
    {
        _callInterceptionHandler =
            callInterceptionHandler ??
                throw new ArgumentNullException(nameof(callInterceptionHandler));

        DynamicMethod =
            dynamicMethod ??
                throw new ArgumentNullException(nameof(dynamicMethod));

        // precompute the value returned by a disposed proxy for the delegate's
        // return type (null for void/reference types, boxed default for value types)
        _disposedReturnValue =
            delegateType.GetMethod("Invoke")?.ReturnType.GetDefaultValue();

        ProxiedDelegate = dynamicMethod.CreateDelegate(delegateType, this);
    }

    /// <summary>
    /// Gets the proxied delegate.
    /// </summary>
    public Delegate ProxiedDelegate { get; private set; }

    /// <summary>
    /// Gets the generated dynamic method that provides the delegate invocation body.
    /// </summary>
    internal DynamicMethod DynamicMethod { get; }

    /// <inheritdoc/>
    public bool IsDisposed => _callInterceptionHandler == null;

    /// <summary>
    /// Method called by delegate proxy, when the proxies delegate is called.
    /// Returns the default value of the delegate's return type when disposed.
    /// </summary>
    /// <param name="args">Arguments passed to the proxied delegate by caller</param>
    /// <returns>Return value provided by call interception handler</returns>
    internal object Intercept(object[] args)
    {
        var handler = _callInterceptionHandler;

        return handler == null
            ? _disposedReturnValue
            : handler(args);
    }

    /// <summary>
    /// Frees managed resources.
    /// </summary>
    public void Dispose()
    {
        ProxiedDelegate = null;
        _callInterceptionHandler = null;
    }
}

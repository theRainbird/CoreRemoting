using System;
using System.Reflection.Emit;
using System.Threading;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Proxy for intercepting calls on a specified delegate type.
/// </summary>
public sealed class DelegateProxy : IDelegateProxy
{
    private Func<object[], object> _callInterceptionHandler;

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

    /// <summary>
    /// Method called by delegate proxy, when the proxies delegate is called.
    /// </summary>
    /// <param name="args">Arguments passed to the proxied delegate by caller</param>
    /// <returns>Return value provided by call interception handler</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the proxy has been disposed</exception>
    internal object Intercept(object[] args)
    {
        var handler = Volatile.Read(ref _callInterceptionHandler);

        if (handler == null)
            throw new ObjectDisposedException(nameof(DelegateProxy));

        return handler(args);
    }

    /// <summary>
    /// Frees managed resources.
    /// </summary>
    public void Dispose()
    {
        ProxiedDelegate = null;
        Interlocked.Exchange(ref _callInterceptionHandler, null);
    }
}
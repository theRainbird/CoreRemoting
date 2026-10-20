using System;
using System.Linq.Expressions;

namespace CoreRemoting;

/// <summary>
/// Interface to be implemented by service invoker classes.
/// A service invoker provides access to a remote service without generating a proxy.
/// </summary>
/// <typeparam name="TService">Type of the remote service contract (interface or class)</typeparam>
public interface IServiceInvoker<TService> : IDisposable where TService : class
{
    /// <summary>
    /// Gets the name of the remote service.
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// Invokes a method on the remote service and returns its result.
    /// </summary>
    /// <param name="call">Expression that selects the method to invoke, including arguments</param>
    /// <typeparam name="TResult">Declared return type of the invoked method</typeparam>
    /// <returns>Result of the remote method invocation</returns>
    TResult Invoke<TResult>(Expression<Func<TService, TResult>> call);

    /// <summary>
    /// Invokes a void method on the remote service.
    /// </summary>
    /// <param name="call">Expression that selects the method to invoke, including arguments</param>
    void Invoke(Expression<Action<TService>> call);
}

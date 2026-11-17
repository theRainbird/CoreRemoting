using System;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Server-side event dispatcher.
/// </summary>
public interface IEventDispatcher
{
    /// <summary>
    /// Invokes the specified event with provided arguments.
    /// </summary>
    /// <param name="getName">
    /// A function that returns event name. Must not use its argument
    /// (e.g. <c>s => nameof(s.SimpleEvent)</c>).
    /// </param>
    /// <param name="arguments">Event arguments.</param>
    /// <typeparam name="TServiceInterface">Service interface.</typeparam>
    void Invoke<TServiceInterface>(Func<TServiceInterface, string> getName, params object[] arguments)
        where TServiceInterface: class;

    /// <summary>
    /// Invokes the specified event with provided arguments.
    /// </summary>
    /// <param name="serviceName">Service name.</param>
    /// <param name="eventName">The event name.</param>
    /// <param name="arguments">Event arguments.</param>
    public void Invoke(string serviceName, string eventName, params object[] arguments);
}

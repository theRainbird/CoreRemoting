using System;
using CoreRemoting.DependencyInjection;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Server-side event dispatcher.
/// </summary>
/// <param name="container">Dependency injection container.</param>
public class EventDispatcher(IDependencyInjectionContainer container) : IEventDispatcher
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
    public void Invoke<TServiceInterface>(Func<TServiceInterface, string> getName, params object[] arguments)
        where TServiceInterface : class
    {
        var serviceName = typeof(TServiceInterface).FullName;
        var reg = container.GetServiceRegistration(serviceName);
        var hub = reg.EventHub?.CreateTyped<TServiceInterface>();
        hub?.Invoke(getName, arguments);
    }

    /// <summary>
    /// Invokes the specified event with provided arguments.
    /// </summary>
    /// <param name="serviceName">Service name.</param>
    /// <param name="eventName">The event name.</param>
    /// <param name="arguments">Event arguments.</param>
    public void Invoke(string serviceName, string eventName, params object[] arguments)
    {
        var reg = container.GetServiceRegistration(serviceName);
        reg.EventHub?.Invoke(eventName, arguments);
    }
}

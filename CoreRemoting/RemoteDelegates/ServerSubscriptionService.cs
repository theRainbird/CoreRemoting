using System;
using CoreRemoting.DependencyInjection;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Default implementation of <see cref="IServerSubscriptionService"/>.
/// </summary>
internal class ServerSubscriptionService : IServerSubscriptionService
{
    private readonly IDependencyInjectionContainer _container;

    /// <summary>
    /// Creates a new instance of the ServerSubscriptionService class.
    /// </summary>
    /// <param name="container">Dependency injection container</param>
    public ServerSubscriptionService(IDependencyInjectionContainer container)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
    }

    /// <inheritdoc/>
    public int RemoveAllForSession(Guid sessionId)
    {
        var count = 0;

        foreach (var registration in _container.GetServiceRegistrations())
            count += registration.EventHub.RemoveAllForSession(sessionId);

        return count;
    }
}

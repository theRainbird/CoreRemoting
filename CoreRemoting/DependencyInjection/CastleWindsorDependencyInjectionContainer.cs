using System;
using System.Collections.Generic;
using Castle.MicroKernel.Lifestyle;
using Castle.MicroKernel.Registration;
using Castle.Windsor;

namespace CoreRemoting.DependencyInjection;

/// <summary>
/// Castle Windsor DI-Container-Wrapper.
/// </summary>
public class CastleWindsorDependencyInjectionContainer : DependencyInjectionContainerBase, IDependencyInjectionContainer
{
    private readonly WindsorContainer _container;

    /// <summary>
    /// Creates a new instance of the CastleWindsorDependencyInjectionContainer class.
    /// </summary>
    public CastleWindsorDependencyInjectionContainer()
    {
        _container = new WindsorContainer();
    }

    /// <summary>
    /// Resolves a service from the dependency injection container.
    /// </summary>
    /// <param name="registration">Service registration</param>
    /// <returns>Resolved service instance</returns>
    protected override object ResolveServiceFromContainer(ServiceRegistration registration)
    {
        var service = _container.Resolve(key: registration.ServiceName, service: registration.InterfaceType);
        registration.EventStub.WireTo(service);
        return service;
    }

    /// <summary>
    /// Resolves Gets a service by a specified interface type.
    /// </summary>
    /// <param name="registration">Service registration</param>
    /// <typeparam name="TServiceInterface">Service interface type</typeparam>
    /// <returns>Service instance</returns>
    protected override TServiceInterface ResolveServiceFromContainer<TServiceInterface>(ServiceRegistration registration)
    {
        var service = _container.Resolve<TServiceInterface>(key: registration.ServiceName);
        registration.EventStub.WireTo(service);
        return service;
    }

    /// <summary>
    /// Registers a service.
    /// </summary>
    /// <param name="lifetime">Service lifetime (Singleton / SingleCall)</param>
    /// <param name="serviceName">Optional unique service name</param>
    /// <typeparam name="TServiceInterface">Service interface type</typeparam>
    /// <typeparam name="TServiceImpl">Service implementation type</typeparam>
    protected override void RegisterServiceInContainer<TServiceInterface, TServiceImpl>(
        ServiceLifetime lifetime,
        string serviceName = "")
    {
        switch (lifetime)
        {
            case ServiceLifetime.Singleton:
                _container.Register(
                    Component
                        .For<TServiceInterface>()
                        .ImplementedBy<TServiceImpl>()
                        .Named(string.IsNullOrEmpty(serviceName) ? typeof(TServiceInterface).FullName : serviceName)
                        .LifestyleSingleton());
                break;

            case ServiceLifetime.SingleCall:
                _container.Register(
                    Component
                        .For<TServiceInterface>()
                        .ImplementedBy<TServiceImpl>()
                        .Named(string.IsNullOrEmpty(serviceName) ? typeof(TServiceInterface).FullName : serviceName)
                        .LifestyleTransient());
                break;

            case ServiceLifetime.Scoped:
                _container.Register(
                    Component
                        .For<TServiceInterface>()
                        .ImplementedBy<TServiceImpl>()
                        .Named(string.IsNullOrEmpty(serviceName) ? typeof(TServiceInterface).FullName : serviceName)
                        .LifestyleScoped());
                break;

            default:
                throw new NotSupportedException("Lifetime not supported: " + lifetime);
        };
    }

    /// <summary>
    /// Registers a service.
    /// </summary>
    /// <param name="factoryDelegate">Factory delegate, which is called to create service instances</param>
    /// <param name="lifetime">Service lifetime (Singleton / SingleCall)</param>
    /// <param name="serviceName">Optional unique service name</param>
    /// <typeparam name="TServiceInterface">Service interface type</typeparam>
    protected override void RegisterServiceInContainer<TServiceInterface>(
        Func<TServiceInterface> factoryDelegate,
        ServiceLifetime lifetime,
        string serviceName = "")
    {
        switch (lifetime)
        {
            case ServiceLifetime.Singleton:
                _container.Register(
                    Component
                        .For<TServiceInterface>()
                        .UsingFactoryMethod(factoryDelegate)
                        .Named(string.IsNullOrEmpty(serviceName) ? typeof(TServiceInterface).FullName : serviceName)
                        .LifestyleSingleton());
                break;

            case ServiceLifetime.SingleCall:
                _container.Register(
                    Component
                        .For<TServiceInterface>()
                        .UsingFactoryMethod(factoryDelegate)
                        .Named(string.IsNullOrEmpty(serviceName) ? typeof(TServiceInterface).FullName : serviceName)
                        .LifestyleTransient());
                break;

            case ServiceLifetime.Scoped:
                _container.Register(
                    Component
                        .For<TServiceInterface>()
                        .UsingFactoryMethod(factoryDelegate)
                        .Named(string.IsNullOrEmpty(serviceName) ? typeof(TServiceInterface).FullName : serviceName)
                        .LifestyleScoped());
                break;

            default:
                throw new NotSupportedException("Lifetime not supported: " + lifetime);
        };
    }

    /// <summary>
    /// Gets whether the specified service is registered or not.
    /// </summary>
    /// <param name="serviceName">Unique service name (Full service interface type name is used, if left blank)</param>
    /// <typeparam name="TServiceInterface">Service interface type</typeparam>
    /// <returns>True, if the service is registered, otherwise false</returns>
    public override bool IsRegistered<TServiceInterface>(string serviceName = "") where TServiceInterface: class
    {
        if (!string.IsNullOrEmpty(serviceName))
            return _container.Kernel.HasComponent(serviceName);

        return _container.Kernel.HasComponent(typeof(TServiceInterface));
    }

    /// <summary>
    /// Gets all registered types (includes non-service types).
    /// </summary>
    /// <returns>Enumerable list of registered types</returns>
    public override IEnumerable<Type> GetAllRegisteredTypes()
    {
        var handlers = _container.Kernel.GetAssignableHandlers(typeof(object));

        var typeList = new List<Type>();

        foreach (var handler in handlers)
        {
            typeList.Add(handler.ComponentModel.Implementation);
            typeList.AddRange(handler.ComponentModel.Services);
        }

        return typeList;
    }

    /// <inheritdoc/>
    public override IDisposable CreateScope() =>
        _container.BeginScope();

    /// <summary>
    /// Frees managed resources.
    /// </summary>
    public override void Dispose()
    {
        base.Dispose();
        _container?.Dispose();
    }
}
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace CoreRemoting.DependencyInjection;

/// <summary>
/// Base class for dependency injection container implementations.
/// </summary>
public abstract class DependencyInjectionContainerBase : IDependencyInjectionContainer
{
    private readonly ConcurrentDictionary<string, ServiceRegistration> _serviceNameRegistry;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    protected DependencyInjectionContainerBase()
    {
        _serviceNameRegistry = new ConcurrentDictionary<string, ServiceRegistration>();
    }

    /// <summary>
    /// Gets registration information about all registered services.
    /// </summary>
    /// <param name="includeHiddenSystemServices">Specifies if hidden system services should be included in the result list</param>
    /// <returns>Enumerable list of service registrations</returns>
    public IEnumerable<ServiceRegistration> GetServiceRegistrations(bool includeHiddenSystemServices = false)
    {
        if (includeHiddenSystemServices)
            return _serviceNameRegistry.Values;
        else
            return _serviceNameRegistry.Values.Where(registration => !registration.IsHiddenSystemService);
    }    
    
    /// <summary>
    /// Returns a service registration by unique service name.
    /// </summary>
    /// <param name="serviceName">Unique service name</param>
    /// <returns>Service registration</returns>
    public ServiceRegistration GetServiceRegistration(string serviceName)
    {
        if (!_serviceNameRegistry.TryGetValue(serviceName, out ServiceRegistration registration))
            throw new KeyNotFoundException($"No service named '{serviceName}' is registered.");

        return registration;
    }

    /// <summary>
    /// Resolves a service from the dependency injection container.
    /// </summary>
    /// <param name="registration">Service registration</param>
    /// <returns>Resolved service instance</returns>
    protected abstract object ResolveServiceFromContainer(ServiceRegistration registration);

    /// <summary>
    /// Resolves Gets a service by a specified interface type.
    /// </summary>
    /// <param name="registration">Service registration</param>
    /// <typeparam name="TServiceInterface">Service interface type</typeparam>
    /// <returns>Service instance</returns>
    protected abstract TServiceInterface ResolveServiceFromContainer<TServiceInterface>(ServiceRegistration registration)
        where TServiceInterface : class;
    
    /// <summary>
    /// Gets the service interface type of a specified service.
    /// </summary>
    /// <param name="serviceName">Unique service name</param>
    /// <returns>Service interface type</returns>
    public Type GetServiceInterfaceType(string serviceName)
    {
        var registration = GetServiceRegistration(serviceName);
        return registration.InterfaceType;
    }
    
    /// <summary>
    /// Gets a service instance by service name.
    /// </summary>
    /// <param name="serviceName">Unique service name</param>
    /// <returns>Service instance</returns>
    public object GetService(string serviceName)
    {
        var registration = GetServiceRegistration(serviceName);
        return ResolveServiceFromContainer(registration);
    }

    /// <summary>
    /// Gets a service instance of a specified interface type.
    /// </summary>
    /// <param name="serviceName">Optional unique service name (Full name of interface type is used, if left blank)</param>
    /// <typeparam name="TServiceInterface">Service interface type</typeparam>
    /// <returns>Service instance</returns>
    public TServiceInterface GetService<TServiceInterface>(string serviceName = "")
        where TServiceInterface : class
    {
        if (string.IsNullOrEmpty(serviceName))
            serviceName = typeof(TServiceInterface).FullName;
        
        var registration = GetServiceRegistration(serviceName);
        return ResolveServiceFromContainer<TServiceInterface>(registration);
    }
    
    /// <summary>
    /// Registers a service in the dependency injection container.
    /// </summary>
    /// <param name="lifetime">Service lifetime (Singleton / SingleCall)</param>
    /// <param name="serviceName">Optional unique service name</param>
    /// <typeparam name="TServiceInterface">Service interface type</typeparam>
    /// <typeparam name="TServiceImpl">Service implementation type</typeparam>
    protected abstract void RegisterServiceInContainer<TServiceInterface, TServiceImpl>(
        ServiceLifetime lifetime, 
        string serviceName = "")
        where TServiceInterface : class
        where  TServiceImpl : class, TServiceInterface;

    /// <summary>
    /// Registers a service in the dependency injection container.
    /// </summary>
    /// <param name="factoryDelegate">Factory delegate, which is called to create service instances</param>
    /// <param name="lifetime">Service lifetime (Singleton / SingleCall)</param>
    /// <param name="serviceName">Optional unique service name</param>
    /// <typeparam name="TServiceInterface">Service interface type</typeparam>
    protected abstract void RegisterServiceInContainer<TServiceInterface>(
        Func<TServiceInterface> factoryDelegate, 
        ServiceLifetime lifetime, 
        string serviceName = "")
        where TServiceInterface : class;

    /// <summary>
    /// Registers a service.
    /// </summary>
    /// <param name="lifetime">Service lifetime (Singleton / SingleCall)</param>
    /// <param name="serviceName">Optional unique service name</param>
    /// <param name="asHiddenSystemService">Specifies if the service should be registered as hidden system service</param>
    /// <typeparam name="TServiceInterface">Service interface type</typeparam>
    /// <typeparam name="TServiceImpl">Service implementation type</typeparam>
    public void RegisterService<TServiceInterface, TServiceImpl>(
        ServiceLifetime lifetime,
        string serviceName = "",
        bool asHiddenSystemService = false)
        where TServiceInterface : class
        where TServiceImpl : class, TServiceInterface
    {
        var serviceInterfaceType = typeof(TServiceInterface);
            
        if (string.IsNullOrWhiteSpace(serviceName))
            serviceName = serviceInterfaceType.FullName;
            
        if (_serviceNameRegistry.ContainsKey(serviceName!))
            return;

        _serviceNameRegistry.TryAdd(
            serviceName,
            new ServiceRegistration(
                serviceName: serviceName,
                interfaceType: serviceInterfaceType,
                implementationType: typeof(TServiceImpl),
                serviceLifetime: lifetime,
                factory: null,
                isHiddenSystemService: asHiddenSystemService));
        
        RegisterServiceInContainer<TServiceInterface, TServiceImpl>(lifetime, serviceName);
    }

    /// <summary>
    /// Registers a service.
    /// </summary>
    /// <param name="factoryDelegate">Factory delegate, which is called to create service instances</param>
    /// <param name="lifetime">Service lifetime (Singleton / SingleCall)</param>
    /// <param name="serviceName">Optional unique service name</param>
    /// <param name="asHiddenSystemService">Specifies if the service should be registered as hidden system service</param>
    /// <typeparam name="TServiceInterface">Service interface type</typeparam>
    public void RegisterService<TServiceInterface>(
        Func<TServiceInterface> factoryDelegate,
        ServiceLifetime lifetime,
        string serviceName = "",
        bool asHiddenSystemService = false)
        where TServiceInterface : class
    {
        var serviceInterfaceType = typeof(TServiceInterface);
            
        if (string.IsNullOrWhiteSpace(serviceName))
            serviceName = serviceInterfaceType.FullName;
            
        if (_serviceNameRegistry.ContainsKey(serviceName!))
            return;

        _serviceNameRegistry.TryAdd(
            serviceName,
            new ServiceRegistration(
                serviceName: serviceName,
                interfaceType: serviceInterfaceType,
                implementationType: null,
                serviceLifetime: lifetime,
                factory: factoryDelegate,
                isHiddenSystemService: asHiddenSystemService));
        
        RegisterServiceInContainer(factoryDelegate, lifetime, serviceName);
    }

    /// <summary>
    /// Gets all registered types (includes non-service types).
    /// </summary>
    /// <returns>Enumerable list of registered types</returns>
    public abstract IEnumerable<Type> GetAllRegisteredTypes();
    
    /// <summary>
    /// Gets whether the specified service is registered or not.
    /// </summary>
    /// <param name="serviceName">Unique service name (Full service interface type name is used, if left blank)</param>
    /// <typeparam name="TServiceInterface">Service interface type</typeparam>
    /// <returns>True, if the service is registered, otherwise false</returns>
    public abstract bool IsRegistered<TServiceInterface>(string serviceName = "") where TServiceInterface : class;

    /// <summary>
    /// Frees managed resources.
    /// </summary>
    public virtual void Dispose()
    {
        _serviceNameRegistry.Clear();
    }
}
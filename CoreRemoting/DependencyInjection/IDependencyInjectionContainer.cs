using System;
using System.Collections.Generic;

namespace CoreRemoting.DependencyInjection;

/// <summary>
/// Interface to be implemented by dependency injection container integration classes.
/// </summary>
public interface IDependencyInjectionContainer : IDisposable
{
    /// <summary>
    /// Gets a service instance by service name.
    /// </summary>
    /// <param name="serviceName">Unique service name</param>
    /// <returns>Service instance</returns>
    object GetService(string serviceName);

    /// <summary>
    /// Gets a service instance of a specified interface type.
    /// </summary>
    /// <param name="serviceName">Optional unique service name (Full name of interface type is used, if left blank)</param>
    /// <typeparam name="TServiceInterface">Service interface type</typeparam>
    /// <returns>Service instance</returns>
    TServiceInterface GetService<TServiceInterface>(string serviceName = "")
        where TServiceInterface : class;

    /// <summary>
    /// Registers a service.
    /// </summary>
    /// <param name="lifetime">Service lifetime (Singleton / SingleCall)</param>
    /// <param name="serviceName">Optional unique service name</param>
    /// <param name="asHiddenSystemService">Specifies if the service should be registered as hidden system service</param>
    /// <typeparam name="TServiceInterface">Service interface type</typeparam>
    /// <typeparam name="TServiceImpl">Service implementation type</typeparam>
    void RegisterService<TServiceInterface, TServiceImpl>(
        ServiceLifetime lifetime,
        string serviceName = "",
        bool asHiddenSystemService = false)
        where TServiceInterface : class
        where TServiceImpl : class, TServiceInterface;

    /// <summary>
    /// Registers a service.
    /// </summary>
    /// <param name="factoryDelegate">Factory delegate, which is called to create service instances</param>
    /// <param name="lifetime">Service lifetime (Singleton / SingleCall)</param>
    /// <param name="serviceName">Optional unique service name</param>
    /// <param name="asHiddenSystemService">Specifies if the service should be registered as hidden system service</param>
    /// <typeparam name="TServiceInterface">Service interface type</typeparam>
    void RegisterService<TServiceInterface>(
        Func<TServiceInterface> factoryDelegate,
        ServiceLifetime lifetime,
        string serviceName = "",
        bool asHiddenSystemService = false)
        where TServiceInterface : class;

    /// <summary>
    /// Gets the service interface type of a specified service.
    /// </summary>
    /// <param name="serviceName">Unique service name</param>
    /// <returns>Service interface type</returns>
    Type GetServiceInterfaceType(string serviceName);

    /// <summary>
    /// Gets all registered types (includes non-service types).
    /// </summary>
    /// <returns>Enumerable list of registered types</returns>
    IEnumerable<Type> GetAllRegisteredTypes();

    /// <summary>
    /// Gets whether the specified service is registered or not.
    /// </summary>
    /// <param name="serviceName">Unique service name (Full service interface type name is used, if left blank)</param>
    /// <typeparam name="TServiceInterface">Service interface type</typeparam>
    /// <returns>True, if the service is registered, otherwise false</returns>
    bool IsRegistered<TServiceInterface>(string serviceName = "") where TServiceInterface : class;

    /// <summary>
    /// Gets registration information about all registered services.
    /// </summary>
    /// <param name="includeHiddenSystemServices">Specifies if hidden system services should be included in the result list</param>
    /// <returns>Enumerable list of service registrations</returns>
    IEnumerable<ServiceRegistration> GetServiceRegistrations(bool includeHiddenSystemServices = false);

    /// <summary>
    /// Returns a service registration by unique service name.
    /// </summary>
    /// <param name="serviceName">Unique service name</param>
    /// <returns>Service registration</returns>
    ServiceRegistration GetServiceRegistration(string serviceName);

    /// <summary>
    /// Creates the scope for scoped services resolution.
    /// </summary>
    IDisposable CreateScope();
}
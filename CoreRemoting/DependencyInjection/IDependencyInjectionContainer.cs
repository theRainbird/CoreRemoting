using System;
using System.Collections.Generic;

namespace CoreRemoting.DependencyInjection
{
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
        /// <typeparam name="TServiceInterface">Service interface type</typeparam>
        /// <typeparam name="TServiceImpl">Service implementation type</typeparam>
        void RegisterService<TServiceInterface, TServiceImpl>(
            ServiceLifetime lifetime, 
            string serviceName = "")
            where TServiceInterface : class
            where  TServiceImpl : class, TServiceInterface;

        /// <summary>
        /// Registers a service.
        /// </summary>
        /// <param name="factoryDelegate">Factory delegate, which is called to create service instances</param>
        /// <param name="lifetime">Service lifetime (Singleton / SingleCall)</param>
        /// <param name="serviceName">Optional unique service name</param>
        /// <typeparam name="TServiceInterface">Service interface type</typeparam>
        void RegisterService<TServiceInterface>(
            Func<TServiceInterface> factoryDelegate, 
            ServiceLifetime lifetime, 
            string serviceName = "")
            where TServiceInterface : class;

        /// <summary>
        /// Gets the service interface type of a specified service.
        /// </summary>
        /// <param name="serviceName">Unique service name</param>
        /// <returns>Service interface type</returns>
        Type GetServiceInterfaceType(string serviceName);

        /// <summary>
        /// Gets all registered types.
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
    }
}
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace CoreRemoting.DependencyInjection
{
    /// <summary>
    /// Microsoft Dependency Injection DI-Container-Wrapper.
    /// </summary>
    public class MicrosoftDependencyInjectionContainer : IDependencyInjectionContainer
    {
        private readonly IServiceCollection _container;
        private IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<string, Type> _serviceNameRegistry;
        private readonly bool _containerCreatedExternally;
        
        /// <summary>
        /// Creates a new instance of the MicrosoftDependencyInjectionContainer class.
        /// </summary>
        /// <param name="serviceCollection">Service collection</param>
        public MicrosoftDependencyInjectionContainer(IServiceCollection serviceCollection = null)
        {
            _containerCreatedExternally = serviceCollection != null;
            
            _serviceNameRegistry = new ConcurrentDictionary<string, Type>();
            _container = serviceCollection ?? new ServiceCollection();

            _serviceProvider = _container.BuildServiceProvider();
        }

        /// <summary>
        /// Gets a service instance by service name.
        /// </summary>
        /// <param name="serviceName">Unique service name</param>
        /// <returns>Service instance</returns>
        public object GetService(string serviceName)
        {
            var serviceInterfaceType = _serviceNameRegistry[serviceName];
            return _serviceProvider.GetRequiredService(serviceInterfaceType);
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
            Type serviceInterfaceType = typeof(TServiceInterface);

            ThrowExceptionIfCustomServiceName(serviceName, serviceInterfaceType);
                
            return _serviceProvider.GetRequiredService<TServiceInterface>();
        }

        [SuppressMessage("ReSharper", "ParameterOnlyUsedForPreconditionCheck.Local")]
        private static void ThrowExceptionIfCustomServiceName(string serviceName, Type serviceInterfaceType)
        {
            if (!string.IsNullOrWhiteSpace(serviceName) && serviceName != serviceInterfaceType.FullName)
                throw new NotSupportedException("Microsoft Dependency Injection does not support named services.");
        }

        /// <summary>
        /// Registers a service.
        /// </summary>
        /// <param name="lifetime">Service lifetime (Singleton / SingleCall)</param>
        /// <param name="serviceName">Optional unique service name</param>
        /// <typeparam name="TServiceInterface">Service interface type</typeparam>
        /// <typeparam name="TServiceImpl">Service implementation type</typeparam>
        [SuppressMessage("ReSharper", "AssignNullToNotNullAttribute")]
        public void RegisterService<TServiceInterface, TServiceImpl>(
            ServiceLifetime lifetime, 
            string serviceName = "")
            where TServiceInterface: class
            where TServiceImpl : class, TServiceInterface
        {
            var serviceInterfaceType = typeof(TServiceInterface);
            
            if (string.IsNullOrWhiteSpace(serviceName))
                serviceName = serviceInterfaceType.FullName;
            
            if (_serviceNameRegistry.ContainsKey(serviceName))
                return;

            _serviceNameRegistry.TryAdd(serviceName, serviceInterfaceType);
            
            switch (lifetime)
            {
                case ServiceLifetime.Singleton:
                    _container.AddSingleton<TServiceInterface, TServiceImpl>();
                    break;
                case ServiceLifetime.SingleCall:
                    _container.AddTransient<TServiceInterface, TServiceImpl>();
                    break;
            }

            _serviceProvider = _container.BuildServiceProvider();
        }
        
        /// <summary>
        /// Registers a service.
        /// </summary>
        /// <param name="factoryDelegate">Factory delegate, which is called to create service instances</param>
        /// <param name="lifetime">Service lifetime (Singleton / SingleCall)</param>
        /// <param name="serviceName">Optional unique service name</param>
        /// <typeparam name="TServiceInterface">Service interface type</typeparam>
        public void RegisterService<TServiceInterface>(Func<TServiceInterface> factoryDelegate, ServiceLifetime lifetime, string serviceName = "")
            where TServiceInterface: class
        {
            var serviceInterfaceType = typeof(TServiceInterface);
            
            if (string.IsNullOrWhiteSpace(serviceName))
                serviceName = serviceInterfaceType.Name;
            
            if (_serviceNameRegistry.ContainsKey(serviceName))
                return;

            _serviceNameRegistry.TryAdd(serviceName, serviceInterfaceType);
            
            switch (lifetime)
            {
                case ServiceLifetime.Singleton:
                    _container.AddSingleton(serviceInterfaceType, factoryDelegate);
                    break;
                case ServiceLifetime.SingleCall:
                    _container.AddTransient(serviceInterfaceType,provider => factoryDelegate());
                    break;
            }
            
            _serviceProvider = _container.BuildServiceProvider();
        }

        /// <summary>
        /// Gets the service interface type of a specified service.
        /// </summary>
        /// <param name="serviceName">Unique service name</param>
        /// <returns>Service interface type</returns>
        public Type GetServiceInterfaceType(string serviceName)
        {
            return _serviceNameRegistry[serviceName];
        }

        /// <summary>
        /// Gets whether the specified service is registered or not.
        /// </summary>
        /// <param name="serviceName">Unique service name (Full service interface type name is used, if left blank)</param>
        /// <typeparam name="TServiceInterface">Service interface type</typeparam>
        /// <returns>True, if the service is registered, otherwise false</returns>
        public bool IsRegistered<TServiceInterface>(string serviceName = "") where TServiceInterface: class
        {
            var serviceInterfaceType = typeof(TServiceInterface);
            
            if (!string.IsNullOrEmpty(serviceName))
                ThrowExceptionIfCustomServiceName(serviceName, serviceInterfaceType);

            
            return _container.Any(descriptor => descriptor.ServiceType == serviceInterfaceType);
        }

        /// <summary>
        /// Gets all registered types.
        /// </summary>
        /// <returns>Enumerable list of registered types</returns>
        public IEnumerable<Type> GetAllRegisteredTypes()
        {
            var typeList = new List<Type>();

            foreach (var serviceDescriptor in _container)
            {
                typeList.Add(serviceDescriptor.ImplementationType);
                typeList.Add(serviceDescriptor.ServiceType);
            }

            return typeList;
        }

        /// <summary>
        /// Frees managed resources.
        /// </summary>
        public void Dispose()
        {
            _serviceNameRegistry.Clear();
            
            if (!_containerCreatedExternally)
                _container.Clear();
        }
    }
}
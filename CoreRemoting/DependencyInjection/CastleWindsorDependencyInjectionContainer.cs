using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Castle.MicroKernel.Registration;
using Castle.Windsor;

namespace CoreRemoting.DependencyInjection
{
    /// <summary>
    /// Castle Windsor DI-Container-Wrapper.
    /// </summary>
    public class CastleWindsorDependencyInjectionContainer : IDependencyInjectionContainer
    {
        private readonly WindsorContainer _container;
        private readonly ConcurrentDictionary<string, Type> _serviceNameRegistry;

        /// <summary>
        /// Creates a new instance of the CastleWindsorDependencyInjectionContainer class.
        /// </summary>
        public CastleWindsorDependencyInjectionContainer()
        {
            _serviceNameRegistry = new ConcurrentDictionary<string, Type>();
            _container = new WindsorContainer();
        }

        /// <summary>
        /// Gets a service instance by service name.
        /// </summary>
        /// <param name="serviceName">Unique service name</param>
        /// <returns>Service instance</returns>
        public object GetService(string serviceName)
        {
            var serviceInterfaceType = _serviceNameRegistry[serviceName];
            return _container.Resolve(key: serviceName, service: serviceInterfaceType);
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

            if (string.IsNullOrWhiteSpace(serviceName))
            {
                serviceName =
                    _serviceNameRegistry
                        .Where(entry => entry.Value.IsAssignableFrom(serviceInterfaceType))
                        .Select(entry => entry.Key)
                        .FirstOrDefault();
            }

            return _container.Resolve<TServiceInterface>(key: serviceName);
        }

        /// <summary>
        /// Registers a service.
        /// </summary>
        /// <param name="lifetime">Service lifetime (Singleton / SingleCall)</param>
        /// <param name="serviceName">Optional unique service name</param>
        /// <typeparam name="TServiceInterface">Service interface type</typeparam>
        /// <typeparam name="TServiceImpl">Service implementation type</typeparam>
        public void RegisterService<TServiceInterface, TServiceImpl>(
            ServiceLifetime lifetime, 
            string serviceName = "")
            where TServiceInterface: class
            where TServiceImpl : class, TServiceInterface
        {
            var serviceInterfaceType = typeof(TServiceInterface);
            
            if (string.IsNullOrWhiteSpace(serviceName))
                serviceName = serviceInterfaceType.FullName;
            
            if (_serviceNameRegistry.ContainsKey(serviceName!))
                return;

            _serviceNameRegistry.TryAdd(serviceName, serviceInterfaceType);
            
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
            }
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
                serviceName = serviceInterfaceType.FullName;
            
            if (_serviceNameRegistry.ContainsKey(serviceName!))
                return;

            _serviceNameRegistry.TryAdd(serviceName, serviceInterfaceType);
            
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
            }
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
            if (!string.IsNullOrEmpty(serviceName))
                return _container.Kernel.HasComponent(serviceName);
                
            return _container.Kernel.HasComponent(typeof(TServiceInterface));
        }

        /// <summary>
        /// Gets all registered types.
        /// </summary>
        /// <returns>Enumerable list of registered types</returns>
        public IEnumerable<Type> GetAllRegisteredTypes()
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

        /// <summary>
        /// Frees managed resources.
        /// </summary>
        public void Dispose()
        {
            _serviceNameRegistry.Clear();
            _container?.Dispose();
        }
    }
}
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Castle.MicroKernel.Registration;
using Castle.Windsor;

namespace CoreRemoting.DependencyInjection
{
    public class CastleWindsorDependencyInjectionContainer : IDependencyInjectionContainer
    {
        private readonly WindsorContainer _container;
        private readonly ConcurrentDictionary<string, Type> _serviceNameRegistry;

        public CastleWindsorDependencyInjectionContainer()
        {
            _serviceNameRegistry = new ConcurrentDictionary<string, Type>();
            _container = new WindsorContainer();
        }

        public object GetService(string serviceName)
        {
            var serviceInterfaceType = _serviceNameRegistry[serviceName];
            return _container.Resolve(key: serviceName, service: serviceInterfaceType);
        }

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
        
        public void RegisterService<TServiceInterface>(Func<TServiceInterface> factoryDelegate, ServiceLifetime lifetime, string serviceName = "")
            where TServiceInterface: class
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
                    _container.Register(
                        Component
                            .For<TServiceInterface>()
                            .UsingFactoryMethod(factoryDelegate)
                            .Named(string.IsNullOrEmpty(serviceName) ? typeof(TServiceInterface).Name : serviceName)
                            .LifestyleSingleton());        
                    break;
                case ServiceLifetime.SingleCall:
                    _container.Register(
                        Component
                            .For<TServiceInterface>()
                            .UsingFactoryMethod(factoryDelegate)
                            .Named(string.IsNullOrEmpty(serviceName) ? typeof(TServiceInterface).Name : serviceName)
                            .LifestyleTransient());
                    break;
            }
        }

        public Type GetServiceInterfaceType(string serviceName)
        {
            return _serviceNameRegistry[serviceName];
        }

        public bool IsRegistered<TServiceInterface>(string serviceName = "") where TServiceInterface: class
        {
            if (!string.IsNullOrEmpty(serviceName))
                return _container.Kernel.HasComponent(serviceName);
                
            return _container.Kernel.HasComponent(typeof(TServiceInterface));
        }

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

        public void Dispose()
        {
            _serviceNameRegistry.Clear();
            _container?.Dispose();
        }
    }
}
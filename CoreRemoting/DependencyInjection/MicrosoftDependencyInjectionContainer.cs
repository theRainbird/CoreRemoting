using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace CoreRemoting.DependencyInjection
{
    /// <summary>
    /// Microsoft Dependency Injection DI-Container-Wrapper.
    /// </summary>
    public class MicrosoftDependencyInjectionContainer : DependencyInjectionContainerBase, IDependencyInjectionContainer
    {
        private readonly IServiceCollection _container;
        private IServiceProvider _serviceProvider;
        private readonly bool _containerCreatedExternally;
        
        /// <summary>
        /// Creates a new instance of the MicrosoftDependencyInjectionContainer class.
        /// </summary>
        /// <param name="serviceCollection">Service collection</param>
        public MicrosoftDependencyInjectionContainer(IServiceCollection serviceCollection = null)
        {
            _containerCreatedExternally = serviceCollection != null;
            _container = serviceCollection ?? new ServiceCollection();

            _serviceProvider = _container.BuildServiceProvider();
        }

        [SuppressMessage("ReSharper", "ParameterOnlyUsedForPreconditionCheck.Local")]
        private static void ThrowExceptionIfCustomServiceName(string serviceName, Type serviceInterfaceType)
        {
            if (!string.IsNullOrWhiteSpace(serviceName) && serviceName != serviceInterfaceType.FullName)
                throw new NotSupportedException("Microsoft Dependency Injection does not support named services.");
        }
        
        /// <summary>
        /// Resolves a service from the dependency injection container.
        /// </summary>
        /// <param name="registration">Service registration</param>
        /// <returns>Resolved service instance</returns>
        protected override object ResolveServiceFromContainer(ServiceRegistration registration)
        {
            ThrowExceptionIfCustomServiceName(registration.ServiceName, registration.InterfaceType);
            return _serviceProvider.GetRequiredService(registration.InterfaceType);
        }

        /// <summary>
        /// Resolves Gets a service by a specified interface type.
        /// </summary>
        /// <param name="registration">Service registration</param>
        /// <typeparam name="TServiceInterface">Service interface type</typeparam>
        /// <returns>Service instance</returns>
        protected override TServiceInterface ResolveServiceFromContainer<TServiceInterface>(ServiceRegistration registration)
        {
            Type serviceInterfaceType = typeof(TServiceInterface);
            ThrowExceptionIfCustomServiceName(registration.ServiceName, serviceInterfaceType);
            
            return _serviceProvider.GetRequiredService<TServiceInterface>();
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
            ThrowExceptionIfCustomServiceName(serviceName, typeof(TServiceInterface));
            
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
        protected override void RegisterServiceInContainer<TServiceInterface>(
            Func<TServiceInterface> factoryDelegate, 
            ServiceLifetime lifetime, 
            string serviceName = "")
        {
            var serviceInterfaceType = typeof(TServiceInterface);
            
            switch (lifetime)
            {
                case ServiceLifetime.Singleton:
                    _container.AddSingleton(serviceInterfaceType, _ => factoryDelegate());
                    break;
                case ServiceLifetime.SingleCall:
                    _container.AddTransient(serviceInterfaceType, _ => factoryDelegate());
                    break;
            }
            
            _serviceProvider = _container.BuildServiceProvider();
        }
        
        /// <summary>
        /// Gets whether the specified service is registered or not.
        /// </summary>
        /// <param name="serviceName">Unique service name (Full service interface type name is used, if left blank)</param>
        /// <typeparam name="TServiceInterface">Service interface type</typeparam>
        /// <returns>True, if the service is registered, otherwise false</returns>
        public override bool IsRegistered<TServiceInterface>(string serviceName = "") where TServiceInterface: class
        {
            var serviceInterfaceType = typeof(TServiceInterface);
            
            if (!string.IsNullOrEmpty(serviceName))
                ThrowExceptionIfCustomServiceName(serviceName, serviceInterfaceType);
            
            return _container.Any(descriptor => descriptor.ServiceType == serviceInterfaceType);
        }

        /// <summary>
        /// Gets all registered types (includes non-service types).
        /// </summary>
        /// <returns>Enumerable list of registered types</returns>
        public override IEnumerable<Type> GetAllRegisteredTypes()
        {
            var typeList = new List<Type>();

            foreach (var serviceDescriptor in _container)
            {
                if (serviceDescriptor.ImplementationType != null) // null for factory register
                    typeList.Add(serviceDescriptor.ImplementationType);
                typeList.Add(serviceDescriptor.ServiceType);
            }

            return typeList;
        }

        /// <summary>
        /// Frees managed resources.
        /// </summary>
        public override void Dispose()
        {
            base.Dispose();
            
            if (!_containerCreatedExternally)
                _container.Clear();
        }
    }
}

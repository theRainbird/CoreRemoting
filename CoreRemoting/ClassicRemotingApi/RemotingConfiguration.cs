using System;
using System.Collections.Concurrent;
using System.Configuration;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using CoreRemoting.ClassicRemotingApi.ConfigSection;
using CoreRemoting.DependencyInjection;

namespace CoreRemoting.ClassicRemotingApi
{
    /// <summary>
    /// Provides CoreRemoting configuration in classic .NET Remoting style.
    /// </summary>
    public static class RemotingConfiguration
    {
        private static ConcurrentDictionary<string, IRemotingServer> _remotingServers = 
            new ConcurrentDictionary<string, IRemotingServer>();

        private static bool _classicRemotingApiDisabled;

        /// <summary>
        /// Disables the Classic Remoting API.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if there is already a server running</exception>
        public static void DisableClassicRemotingApi()
        {
            if (_classicRemotingApiDisabled)
                return;

            if (_remotingServers.Count > 0)
                throw new InvalidOperationException(
                    "Classic Remoting API can not be disabled, because there is at least one server running.");
            
            _classicRemotingApiDisabled = true;

            _remotingServers = null;
        }

        /// <summary>
        /// Enables the Classic Remoting API.
        /// </summary>
        public static void EnableClassicRemotingApi()
        {
            if (!_classicRemotingApiDisabled)
                return;
            
            _remotingServers = 
                new ConcurrentDictionary<string, IRemotingServer>();
            
            _classicRemotingApiDisabled = false;
        }

        /// <summary>
        /// Gets whether the classic Remoting API is disabled or not.
        /// </summary>
        public static bool IsClassicRemotingApiDisabled => _classicRemotingApiDisabled;
        
        /// <summary>
        /// Registers a CoreRemoting server in the centralized server collection.
        /// </summary>
        /// <param name="server">CoreRemoting server</param>
        /// <exception cref="DuplicateNameException">Thrown if a server with the same unique instance name is already registered</exception>
        public static void RegisterServer(IRemotingServer server)
        {
            if (_classicRemotingApiDisabled)
                return;
            
            if (!_remotingServers.TryAdd(server.UniqueServerInstanceName, server))
                throw new DuplicateNameException(
                    $"A server with unique instance name '{server.UniqueServerInstanceName}' is already registered.");
        }

        /// <summary>
        /// Removes a registered server from the centralized server collection.
        /// </summary>
        /// <param name="server"></param>
        public static void UnregisterServer(IRemotingServer server)
        {
            if (_classicRemotingApiDisabled)
                return;
            
            _remotingServers.TryRemove(server.UniqueServerInstanceName, out _);
        }

        /// <summary>
        /// Gets a registered server instance by its unique name.
        /// </summary>
        /// <param name="uniqueServerInstanceName">Unique server instance name</param>
        /// <returns>CoreRemoting server</returns>
        public static IRemotingServer GetRegisteredServer(string uniqueServerInstanceName)
        {
            if (_classicRemotingApiDisabled)
                return null;
            
            if (string.IsNullOrWhiteSpace(uniqueServerInstanceName))
                return DefaultRemotingInfrastructure.DefaultRemotingServer;

            _remotingServers.TryGetValue(uniqueServerInstanceName, out IRemotingServer server);

            return server;
        }

        /// <summary>
        /// Registers a service.
        /// </summary>
        /// <param name="entry">Service configuration data</param>
        /// <exception cref="InvalidOperationException">Thrown if the Classic Remoting API is disabled</exception>
        /// <exception cref="ArgumentNullException">Thrown if parameter 'entry' is null</exception>
        [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
        public static void RegisterWellKnownServiceType(WellKnownServiceTypeEntry entry)
        {
            if (_classicRemotingApiDisabled)
                throw new InvalidOperationException("Classic Remoting API is disabled.");
            
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            var interfaceAssembly = Assembly.Load(entry.InterfaceAssemblyName);
            var interfaceType = interfaceAssembly.GetType(entry.InterfaceTypeName);
            
            var implementationAssembly = Assembly.Load(entry.ImplementationAssemblyName);
            var implementationType = implementationAssembly.GetType(entry.ImplementationTypeName);

            RegisterWellKnownServiceType(
                interfaceType, 
                implementationType, 
                entry.Lifetime, 
                entry.ServiceName, 
                entry.UniqueServerInstanceName);
        }

        /// <summary>
        /// Registers a service.
        /// </summary>
        /// <param name="interfaceType">Service interface type</param>
        /// <param name="implementationType">Service implementation type</param>
        /// <param name="lifetime">LIfetime (SingleCall / Singleton)</param>
        /// <param name="serviceName">Unique name of the service (Full name of interface type is used, if left blank)</param>
        /// <param name="uniqueServerInstanceName">Unique instance name of the CoreRemoting server that should used for hosting this service</param>
        /// <exception cref="InvalidOperationException">Thrown if the Classic Remoting API is disabled</exception>
        public static void RegisterWellKnownServiceType(
            Type interfaceType,
            Type implementationType,
            ServiceLifetime lifetime,
            string serviceName = "",
            string uniqueServerInstanceName = "")
        {
            if (_classicRemotingApiDisabled)
                throw new InvalidOperationException("Classic Remoting API is disabled.");
            
            var server = 
                string.IsNullOrWhiteSpace(uniqueServerInstanceName) 
                    ? DefaultRemotingInfrastructure.DefaultRemotingServer
                    : GetRegisteredServer(uniqueServerInstanceName);
            
            var container = server.ServiceRegistry;

            var registerServiceMethod =
                container.GetRegisterServiceMethodForWellknownServiceType(interfaceType, implementationType);

            registerServiceMethod.Invoke(container, new object[]{ lifetime, serviceName });
        }

        /// <summary>
        /// Applies CoreRemoting configuration from config file. 
        /// </summary>
        /// <param name="fileName">Path to XML configuration file (Default EXE configuration file will be used, if empty)</param>
        [SuppressMessage("ReSharper", "ObjectCreationAsStatement")]
        public static void Configure(string fileName = "")
        {
            if (_classicRemotingApiDisabled)
                throw new InvalidOperationException("Classic Remoting API is disabled.");
            
            Configuration configuration =
                string.IsNullOrWhiteSpace(fileName)
                    ? ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None)
                    : ConfigurationManager.OpenMappedExeConfiguration(
                        fileMap: new ExeConfigurationFileMap() {ExeConfigFilename = fileName},
                        userLevel: ConfigurationUserLevel.None);

            var configSection = (CoreRemotingConfigSection) 
                configuration.Sections["coreRemoting"];

            foreach (ServerInstanceConfigElement serverInstanceConfig in configSection.ServerInstances)
            {
                var serverConfig = serverInstanceConfig.ToServerConfig();
                new RemotingServer(serverConfig);
            }
            
            foreach (WellKnownServiceConfigElement serviceConfigElement in configSection.Services)
            {
                var entry = serviceConfigElement.ToWellKnownServiceTypeEntry();
                RegisterWellKnownServiceType(entry);
            }
        }

        /// <summary>
        /// Shutdown all registered servers.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if the Classic Remoting API is disabled</exception>
        public static void ShutdownAll()
        {
            if (_classicRemotingApiDisabled)
                throw new InvalidOperationException("Classic Remoting API is disabled.");
            
            foreach (var registeredServer in _remotingServers.ToArray())
            {
                registeredServer.Value.Dispose();
            }
        }
    }
}
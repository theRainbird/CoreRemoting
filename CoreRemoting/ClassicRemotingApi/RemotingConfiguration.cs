using System;
using System.Configuration;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using CoreRemoting.Authentication;
using CoreRemoting.ClassicRemotingApi.ConfigSection;
using CoreRemoting.DependencyInjection;

namespace CoreRemoting.ClassicRemotingApi
{
    /// <summary>
    /// Provides CoreRemoting configuration in classic .NET Remoting style.
    /// </summary>
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "ObjectCreationAsStatement")]
    public static class RemotingConfiguration
    {
        /// <summary>
        /// Gets a list of currently registered CoreRemoting server instances.
        /// </summary>
        public static IRemotingServer[] RegisteredServerInstances => RemotingServer.ActiveServerInstances.ToArray();

        /// <summary>
        /// Gets a list of currently registered CoreRemoting client instances.
        /// </summary>
        public static IRemotingClient[] RegisteredClientInstances => RemotingClient.ActiveClientInstances.ToArray();
        
        /// <summary>
        /// Gets a registered server instance by its unique name.
        /// </summary>
        /// <param name="uniqueServerInstanceName">Unique server instance name</param>
        /// <returns>CoreRemoting server</returns>
        public static IRemotingServer GetRegisteredServer(string uniqueServerInstanceName)
        {
            if (string.IsNullOrWhiteSpace(uniqueServerInstanceName))
                return RemotingServer.DefaultRemotingServer;

            return
                RemotingServer
                    .ActiveServerInstances
                    .FirstOrDefault(instance =>
                        instance.Config.UniqueServerInstanceName.Equals(uniqueServerInstanceName));
        }
        
        /// <summary>
        /// Gets a registered client instance by its unique name.
        /// </summary>
        /// <param name="uniqueClientInstanceName">Unique client instance name</param>
        /// <returns>CoreRemoting client</returns>
        public static IRemotingClient GetRegisteredClient(string uniqueClientInstanceName)
        {
            if (string.IsNullOrWhiteSpace(uniqueClientInstanceName))
                return RemotingClient.DefaultRemotingClient;

            return
                RemotingClient
                    .ActiveClientInstances
                    .FirstOrDefault(instance =>
                        instance.Config.UniqueClientInstanceName.Equals(uniqueClientInstanceName));
        }

        /// <summary>
        /// Registers a new CoreRemoting server instance and returns it's unique instance name.
        /// </summary>
        /// <param name="config">Server configuration</param>
        /// <returns>Unique server instance name</returns>
        public static string RegisterServer(ServerConfig config)
        {
            var server = new RemotingServer(config);
            return server.Config.UniqueServerInstanceName;
        }
        
        /// <summary>
        /// Registers a new CoreRemoting client instance and returns it's unique instance name.
        /// </summary>
        /// <param name="config">Client configuration</param>
        /// <returns>Unique client instance name</returns>
        public static string RegisterClient(ClientConfig config)
        {
            var client = new RemotingClient(config);
            return client.Config.UniqueClientInstanceName;
        }

        /// <summary>
        /// Unregisters a CoreRemoting server.
        /// </summary>
        /// <param name="uniqueServerInstanceName">Unique name of the server instance</param>
        public static void UnregisterServer(string uniqueServerInstanceName)
        {
            var server = RemotingServer.GetActiveServerInstance(uniqueServerInstanceName);
            server?.Dispose();
        }
        
        /// <summary>
        /// Unregisters a CoreRemoting client.
        /// </summary>
        /// <param name="uniqueClientInstanceName">Unique name of the client instance</param>
        public static void UnregisterClient(string uniqueClientInstanceName)
        {
            var client = RemotingClient.GetActiveClientInstance(uniqueClientInstanceName);
            client?.Dispose();
        }

        /// <summary>
        /// Registers a service.
        /// </summary>
        /// <param name="entry">Service configuration data</param>
        /// <exception cref="ArgumentNullException">Thrown if parameter 'entry' is null</exception>
        [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
        public static void RegisterWellKnownServiceType(WellKnownServiceTypeEntry entry)
        {
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
        public static void RegisterWellKnownServiceType(
            Type interfaceType,
            Type implementationType,
            ServiceLifetime lifetime,
            string serviceName = "",
            string uniqueServerInstanceName = "")
        {
            var server = 
                string.IsNullOrWhiteSpace(uniqueServerInstanceName) 
                    ? RemotingServer.DefaultRemotingServer
                    : GetRegisteredServer(uniqueServerInstanceName);
            
            var container = server.ServiceRegistry;

            var registerServiceMethod =
                container.GetRegisterServiceMethodForWellknownServiceType(interfaceType, implementationType);

            registerServiceMethod.Invoke(container, new object[]{ lifetime, serviceName });
        }

        /// <summary>
        /// Applies CoreRemoting server configuration from config file. 
        /// </summary>
        /// <param name="fileName">Path to XML configuration file (Default EXE configuration file will be used, if empty)</param>
        /// <param name="credentials">Optional credentials for authentication</param>
        [SuppressMessage("ReSharper", "ObjectCreationAsStatement")]
        public static void Configure(string fileName = "", Credential[] credentials = null)
        {
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
                var server = new RemotingServer(serverConfig);
                server.Start();
            }
            
            foreach (WellKnownServiceConfigElement serviceConfigElement in configSection.Services)
            {
                var entry = serviceConfigElement.ToWellKnownServiceTypeEntry();
                RegisterWellKnownServiceType(entry);
            }
            
            foreach (ClientInstanceConfigElement clientInstanceConfig in configSection.ClientInstances)
            {
                var clientConfig = clientInstanceConfig.ToClientConfig();
                clientConfig.Credentials = credentials;
                new RemotingClient(clientConfig);
            }
        }

        /// <summary>
        /// Shutdown all registered clients and servers.
        /// </summary>
        public static void ShutdownAll()
        {
            foreach (var registeredClient in RegisteredClientInstances)
                registeredClient.Dispose();
            
            foreach (var registeredServer in RegisteredServerInstances)
                registeredServer.Dispose();
        }
    }
}
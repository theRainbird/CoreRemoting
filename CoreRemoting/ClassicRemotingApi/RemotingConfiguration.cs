using System;
using System.Configuration;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
        /// <summary>
        /// Gets a registered server instance by its unique name.
        /// </summary>
        /// <param name="uniqueServerInstanceName">Unique server instance name</param>
        /// <returns>CoreRemoting server</returns>
        public static IRemotingServer GetRegisteredServer(string uniqueServerInstanceName)
        {
            if (string.IsNullOrWhiteSpace(uniqueServerInstanceName))
                return DefaultRemotingInfrastructure.DefaultRemotingServer;

            return
                RemotingServer
                    .ActiveServerInstances
                    .FirstOrDefault(instance =>
                        instance.Config.UniqueServerInstanceName.Equals(uniqueServerInstanceName));
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
                    ? DefaultRemotingInfrastructure.DefaultRemotingServer
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
        [SuppressMessage("ReSharper", "ObjectCreationAsStatement")]
        public static void Configure(string fileName = "")
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
                new RemotingServer(serverConfig);
            }
            
            foreach (WellKnownServiceConfigElement serviceConfigElement in configSection.Services)
            {
                var entry = serviceConfigElement.ToWellKnownServiceTypeEntry();
                RegisterWellKnownServiceType(entry);
            }
            
            foreach (ClientInstanceConfigElement clientInstanceConfig in configSection.ClientInstances)
            {
                var clientConfig = clientInstanceConfig.ToClientConfig();
                new RemotingClient(clientConfig);
            }
        }

        /// <summary>
        /// Shutdown all registered servers.
        /// </summary>
        public static void ShutdownAll()
        {
            foreach (var registeredServer in RemotingServer.ActiveServerInstances.ToArray())
            {
                registeredServer.Dispose();
            }
        }
    }
}
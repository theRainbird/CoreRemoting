using System;
using System.Collections.Concurrent;
using System.Configuration;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using CoreRemoting.ClassicRemotingApi.ConfigSection;
using CoreRemoting.DependencyInjection;

namespace CoreRemoting.ClassicRemotingApi
{
    public static class RemotingConfiguration
    {
        private static ConcurrentDictionary<string, IRemotingServer> _remotingServers = 
            new ConcurrentDictionary<string, IRemotingServer>();

        private static bool _classicRemotingApiDisabled = false;

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

        public static void EnableClassicRemotingApi()
        {
            if (!_classicRemotingApiDisabled)
                return;
            
            _remotingServers = 
                new ConcurrentDictionary<string, IRemotingServer>();
            
            _classicRemotingApiDisabled = false;
        }

        public static void RegisterServer(IRemotingServer server)
        {
            if (_classicRemotingApiDisabled)
                return;
            
            if (!_remotingServers.TryAdd(server.UniqueServerInstanceName, server))
                throw new DuplicateNameException(
                    $"A server with unique instance name '{server.UniqueServerInstanceName}' is already registerd.");
        }

        public static void UnregisterServer(IRemotingServer server)
        {
            if (_classicRemotingApiDisabled)
                return;
            
            _remotingServers.TryRemove(server.UniqueServerInstanceName, out IRemotingServer removedServer);
        }

        public static IRemotingServer GetRegisteredServer(string uniqueServiceInstanceName)
        {
            if (_classicRemotingApiDisabled)
                return null;
            
            if (string.IsNullOrWhiteSpace(uniqueServiceInstanceName))
                return DefaultRemotingInfrastructure.DefaultRemotingServer;

            _remotingServers.TryGetValue(uniqueServiceInstanceName, out IRemotingServer server);

            return server;
        }

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
                container
                    .GetType()
                    .GetMethods()
                    .Where(m =>
                        m.Name == "RegisterService" &&
                        m.IsGenericMethodDefinition)
                    .Select(m => new
                    {
                        Method = m,
                        Params = m.GetParameters(),
                        Args = m.GetGenericArguments()
                    })
                    .Where(x =>
                        x.Params.Length == 2 &&
                        x.Args.Length == 2)
                    .Select(x => x.Method)
                    .First()
                    .MakeGenericMethod(interfaceType, implementationType);

            registerServiceMethod.Invoke(container, new object[]{ lifetime, serviceName });
        }

        /// <summary>
        /// Applies CoreRemoting configuration from config file. 
        /// </summary>
        /// <param name="fileName">Path to XML configuration file (Default EXE configuration file will be used, if empty)</param>
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
                var server = new RemotingServer(serverConfig);
            }
            
            foreach (WellKnownServiceConfigElement serviceConfigElement in configSection.Services)
            {
                var entry = serviceConfigElement.ToWellKnownServiceTypeEntry();
                RegisterWellKnownServiceType(entry);
            }
        }

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
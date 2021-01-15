using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using CoreRemoting.DependencyInjection;

namespace CoreRemoting.ClassicRemotingApi
{
    public static class RemotingConfiguration
    {
        private static readonly ConcurrentDictionary<string, IRemotingServer> RemotingServers = 
            new ConcurrentDictionary<string, IRemotingServer>();

        public static void RegisterServer(IRemotingServer server)
        {
            if (!RemotingServers.TryAdd(server.UniqueServerInstanceName, server))
                throw new DuplicateNameException(
                    $"A server with unique instance name '{server.UniqueServerInstanceName}' is already registerd.");
        }

        public static void UnregisterServer(IRemotingServer server)
        {
            RemotingServers.TryRemove(server.UniqueServerInstanceName, out IRemotingServer removedServer);
        }

        public static IRemotingServer GetRegisteredServer(string uniqueServiceInstanceName)
        {
            if (string.IsNullOrWhiteSpace(uniqueServiceInstanceName))
                return DefaultRemotingInfrastructure.Singleton.DefaultRemotingServer;

            RemotingServers.TryGetValue(uniqueServiceInstanceName, out IRemotingServer server);

            return server;
        }

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

        public static void RegisterWellKnownServiceType(
            Type interfaceType,
            Type implementationType,
            ServiceLifetime lifetime,
            string serviceName = "",
            string uniqueServerInstanceName = "")
        {
            var server = 
                string.IsNullOrWhiteSpace(uniqueServerInstanceName) 
                    ? DefaultRemotingInfrastructure.Singleton.DefaultRemotingServer
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
    }
}
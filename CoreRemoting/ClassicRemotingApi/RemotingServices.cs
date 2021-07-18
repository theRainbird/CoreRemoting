using System;
using System.Collections.Generic;
using System.Reflection;
using CoreRemoting.DependencyInjection;
using CoreRemoting.RemoteDelegates;

namespace CoreRemoting.ClassicRemotingApi
{
    /// <summary>
    /// Provides several methods for using and publishing remoted objects and proxies.
    /// </summary>
    public static class RemotingServices
    {
        /// <summary>
        /// Returns a Boolean value that indicates whether the client that called the method specified in the given message is waiting for the server to finish processing the method before continuing execution.
        /// </summary>
        /// <param name="method">The method in question</param>
        /// <returns>True if the method is one way; otherwise, false.</returns>
        public static bool IsOneWay(MethodBase method)
        {
            return method.GetCustomAttribute<OneWayAttribute>() != null;
        }

        /// <summary>
        /// Returns a Boolean value that indicates whether the given object is a transparent proxy or a real object.
        /// </summary>
        /// <param name="proxy">The reference to the object to check.</param>
        /// <returns>A Boolean value that indicates whether the object specified in the proxy parameter is a transparent proxy or a real object.</returns>
        public static bool IsTransparentProxy(object proxy)
        {
            return Castle.DynamicProxy.ProxyUtil.IsProxy(proxy);
        }

        /// <summary>
        /// Registers a object as CoreRemoting service.
        /// </summary>
        /// <param name="serviceInstance">Object instance that should be registered as service</param>
        /// <param name="serviceName">Unique service name (Interface full type name is used, if left blank)</param>
        /// <param name="interfaceType">Service interface type</param>
        /// <param name="uniqueServerInstanceName">Unique server instance name</param>
        /// <returns>Service name</returns>
        public static string Marshal(
            object serviceInstance, 
            string serviceName,
            Type interfaceType, 
            string uniqueServerInstanceName = "")
        {
            var server = 
                string.IsNullOrWhiteSpace(uniqueServerInstanceName) 
                    ? RemotingServer.DefaultRemotingServer
                    : RemotingConfiguration.GetRegisteredServer(uniqueServerInstanceName);
            
            var container = server.ServiceRegistry;
            
            var registerServiceMethod =
                container.GetRegisterServiceMethodForServiceInstance(interfaceType, serviceInstance);

            if (string.IsNullOrWhiteSpace(serviceName))
                serviceName = interfaceType.FullName;

            var genericFunc = typeof(Func<>);
            var factoryFunc = genericFunc.MakeGenericType(interfaceType);
            var factoryFuncProxy =
                new DelegateProxy(factoryFunc, delegateArgs => serviceInstance);
            
            registerServiceMethod.Invoke(container, new object[]
            {
                factoryFuncProxy.ProxiedDelegate,
                ServiceLifetime.Singleton, 
                serviceName
            });

            return serviceName;
        }

        /// <summary>
        /// Creates a proxy for a remote CoreRemoting service.
        /// </summary>
        /// <param name="interfaceType">Service interface type</param>
        /// <param name="serviceName">Optional service name</param>
        /// <param name="uniqueClientInstanceName">Unique client instance name (Default client is used if empty)</param>
        /// <returns>Proxy</returns>
        public static object Connect(Type interfaceType, string serviceName = "", string uniqueClientInstanceName = "")
        {
            var client =
                string.IsNullOrWhiteSpace(uniqueClientInstanceName)
                    ? RemotingClient.DefaultRemotingClient
                    : RemotingClient.GetActiveClientInstance(uniqueClientInstanceName);

            if (client == null)
                throw new KeyNotFoundException(
                    $"No remoting client instance named '{uniqueClientInstanceName}' found.");

            if (!client.IsConnected)
            {
                client.Connect();
            }

            return client.CreateProxy(interfaceType, serviceName);
        }
    }
}
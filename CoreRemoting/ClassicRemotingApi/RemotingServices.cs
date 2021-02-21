using System;
using System.Reflection;
using CoreRemoting.DependencyInjection;
using CoreRemoting.RemoteDelegates;
using Newtonsoft.Json.Schema;

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
        /// <param name="serviceInstance">Object instance that should be registred as service</param>
        /// <param name="interfaceType">Service interface type</param>
        /// <param name="uniqueServerInstanceName">Unique server instance name</param>
        /// <returns>Service name</returns>
        /// <exception cref="InvalidOperationException">Thrown if classic Remoting API is diabled</exception>
        public static string Marshal(
            object serviceInstance, 
            Type interfaceType, 
            string uniqueServerInstanceName = "")
        {
            if (RemotingConfiguration.IsClassicRemotingApiDisabled)
                throw new InvalidOperationException("Classic Remoting API is disabled.");
            
            var server = 
                string.IsNullOrWhiteSpace(uniqueServerInstanceName) 
                    ? DefaultRemotingInfrastructure.DefaultRemotingServer
                    : RemotingConfiguration.GetRegisteredServer(uniqueServerInstanceName);
            
            var container = server.ServiceRegistry;
            
            var registerServiceMethod =
                container.GetRegisterServiceMethodForServiceInstance(interfaceType, serviceInstance);

            var serviceName = interfaceType.Name + "_" + Guid.NewGuid().ToString();

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
    }
}
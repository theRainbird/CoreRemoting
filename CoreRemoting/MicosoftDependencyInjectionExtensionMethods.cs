using System;
using System.Diagnostics.CodeAnalysis;
using CoreRemoting.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace CoreRemoting
{
    /// <summary>
    /// Provides extension methods for adding CoreRemoting as service to Microsoft dependency injection container.
    /// </summary>
    public static class MicosoftDependencyInjectionExtensionMethods
    {
        /// <summary>
        /// Adds a CoreRemoting client as singleton to the service collection of a Microsoft dependency injection container.
        /// </summary>
        /// <param name="services">Service collection to which the client should be added</param>
        /// <param name="config">Configuration settings for the CoreRemoting client</param>
        /// <param name="remoteServiceInterfaceTypes">Array of remote service interface types for which proxy objects are to be added</param>
        public static void AddCoreRemotingClient(this IServiceCollection services, ClientConfig config, params Type[] remoteServiceInterfaceTypes)
        {
            var client = new RemotingClient(config);
            client.Connect();
            services.AddSingleton(client);

            foreach (var remoteServiceInterfaceType in remoteServiceInterfaceTypes)
            {
                services.AddSingleton(
                    serviceType: remoteServiceInterfaceType,
                    implementationFactory: _ => client.CreateProxy(remoteServiceInterfaceType));
            }
        }

        /// <summary>
        /// Adds a CoreRemoting server as singleton to the service collection of a Microsoft dependency injection container.
        /// </summary>
        /// <param name="services">Service collection to which the server should be added</param>
        /// <param name="config">Configuration settings for the CoreRemoting server</param>
        [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
        public static void AddCoreRemotingServer(this IServiceCollection services, ServerConfig config)
        {
            config.DependencyInjectionContainer = new MicrosoftDependencyInjectionContainer(services);
            
            var server = new RemotingServer(config);
            services.AddSingleton<IRemotingServer>(server);
        }

        /// <summary>
        /// Adds a CoreRemoting server with default config but a specified network port as singleton
        /// to the service collection of a Microsoft dependency injection container.
        /// </summary>
        /// <param name="services">Service collection to which the server should be added</param>
        /// <param name="networkPort">Network port on which the server should be listening for client requests</param>
        public static void AddCoreRemotingServer(this IServiceCollection services, int networkPort)
        {
            var config = new ServerConfig
            {
                NetworkPort = networkPort
            };

            services.AddCoreRemotingServer(config);
        }
    }
}
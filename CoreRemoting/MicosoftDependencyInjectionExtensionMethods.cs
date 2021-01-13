using System;
using System.Diagnostics.CodeAnalysis;
using CoreRemoting.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace CoreRemoting
{
    public static class MicosoftDependencyInjectionExtensionMethods
    {
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

        [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
        public static void AddCoreRemotingServer(this IServiceCollection services, ServerConfig config)
        {
            config.DependencyInjectionContainer = new MicrosoftDependencyInjectionContainer(services);
            
            var server = new RemotingServer(config);
            services.AddSingleton<IRemotingServer>(server);
        }

        public static void AddCoreRemotingServer(this IServiceCollection services, int networkPort)
        {
            var config = new ServerConfig()
            {
                NetworkPort = networkPort
            };

            services.AddCoreRemotingServer(config);
        }
    }
}
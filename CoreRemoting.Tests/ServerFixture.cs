using System;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;

namespace CoreRemoting.Tests;

public class ServerFixture : IDisposable
{
    public ServerFixture()
    {
        TestService = new TestService();
        
        var serverConfig =
            new ServerConfig()
            {
                UniqueServerInstanceName = "Server1",
                IsDefault = true,
                MessageEncryption = false,
                NetworkPort = 9094,
                RegisterServicesAction = container =>
                {
                    // Service with fakable methods
                    container.RegisterService<ITestService>(
                        factoryDelegate: () => TestService,
                        lifetime: ServiceLifetime.Singleton);

                    // Service for async tests
                    container.RegisterService<IAsyncService, AsyncService>(
                        lifetime: ServiceLifetime.Singleton);

                    // Service for Linq expression tests
                    container.RegisterService<IHobbitService, HobbitService>(
                        lifetime: ServiceLifetime.Singleton);
                    
                    // Service for testing return as proxy
                    container.RegisterService<IFactoryService, FactoryService>(
                        lifetime: ServiceLifetime.Singleton);
                    
                    // Service for generic method tests
                    container.RegisterService<IGenericEchoService, GenericEchoService>(
                        lifetime: ServiceLifetime.Singleton);
                    
                    // Service for enum tests
                    container.RegisterService<IEnumTestService, EnumTestService>(
                        lifetime: ServiceLifetime.Singleton);
                }
            };
            
        Server = new RemotingServer(serverConfig);
        Server.Start();
    }
    
    public RemotingServer Server { get; }
    public TestService TestService { get; }
    
    public void Dispose()
    {
        if (Server != null)
            Server.Dispose();
    }
}
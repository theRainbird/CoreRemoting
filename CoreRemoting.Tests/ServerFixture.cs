using System;
using System.Threading;
using CoreRemoting.Channels;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests;

public class ServerFixture : IDisposable
{
    public int ServerErrorCount;
    
    public ServerFixture()
    {
        TestService = new TestService();
        
        ServerConfig =
            new ServerConfig()
            {
                UniqueServerInstanceName = "DefaultServer",
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

                    // Failing constructor service
                    container.RegisterService<IFailingService, FailingService>(
                        lifetime: ServiceLifetime.SingleCall);

                    // Service for session tests
                    container.RegisterService<ISessionAwareService, SessionAwareService>(
                        lifetime: ServiceLifetime.SingleCall);
                }
            };
    }

    public void Start(IServerChannel channel = null)
    {
        if (Server != null)
            return;

        if (channel != null)
            ServerConfig.Channel = channel;

        Server = new RemotingServer(ServerConfig);
        Server.Error += (_, _) =>
        {
            ServerErrorCount++;
        };

        Server.Start();
    }

    public RemotingServer Server { get; private set;  }

    public ServerConfig ServerConfig { get; set; }

    public TestService TestService { get; }

    public void Dispose()
    {
        if (Server != null)
        {
            Thread.Sleep(100); // work around WatsonTcp 6.0.2 bug
            Server.Dispose();
        }
    }
}

[CollectionDefinition("CoreRemoting")]
public class ServerCollection : ICollectionFixture<ServerFixture>
{
}
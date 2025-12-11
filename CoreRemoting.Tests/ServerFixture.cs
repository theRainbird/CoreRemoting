using System;
using System.Threading;
using CoreRemoting.Channels;
using CoreRemoting.DependencyInjection;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.Tests.Tools;
using CoreRemoting.Threading;
using Xunit;
using CoreRemoting; // RemotingClient
using CoreRemoting.ClassicRemotingApi; // RemotingConfiguration

namespace CoreRemoting.Tests;

public class ServerFixture : IDisposable
{
    public int ServerErrorCount;

    public Exception LastServerError;

    private static int _nextNetworkPort = 9099;
    
    public ServerFixture()
    {
        TestService = new TestService();

        Interlocked.Increment(ref _nextNetworkPort);
        
        ServerConfig =
            new ServerConfig()
            {
                UniqueServerInstanceName = "DefaultServer",
                IsDefault = true,
                MessageEncryption = false,
                NetworkPort = _nextNetworkPort,
                KeySize = 1024,
                RegisterServicesAction = container =>
                {
                    // Service with fakable methods
                    container.RegisterService<ITestService>(
                        factoryDelegate: () => TestService,
                        lifetime: ServiceLifetime.Singleton);

                    // Services for event tests
                    container.RegisterService<ITestService, TestService>(
                        lifetime: ServiceLifetime.Singleton,
                        serviceName: "TestService_Singleton_Service");
                    container.RegisterService<ITestService, TestService>(
                        lifetime: ServiceLifetime.SingleCall,
                        serviceName: "TestService_SingleCall_Service");
                    container.RegisterService<ITestService, TestService>(
                        lifetime: ServiceLifetime.Scoped,
                        serviceName: "TestService_Scoped_Service");
                    container.RegisterService<ITestService>(
                        factoryDelegate: () => new TestService(),
                        lifetime: ServiceLifetime.Singleton,
                        serviceName: "TestService_Singleton_Factory");
                    container.RegisterService<ITestService>(
                        factoryDelegate: () => new TestService(),
                        lifetime: ServiceLifetime.SingleCall,
                        serviceName: "TestService_SingleCall_Factory");
                    container.RegisterService<ITestService>(
                        factoryDelegate: () => new TestService(),
                        lifetime: ServiceLifetime.Scoped,
                        serviceName: "TestService_Scoped_Factory");

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

                    // Services for the remote call scope tests
                    container.RegisterService<IScopedService, ScopedService>(
                        lifetime: ServiceLifetime.Scoped);
                    container.RegisterService<IServiceWithDeps, ServiceWithDeps>(
                        lifetime: ServiceLifetime.SingleCall);
                }
            };

        SafeDynamicInvoker.ThreadPool = new SimpleLockThreadPool(Environment.ProcessorCount + 1);
    }

    public void Start(IServerChannel channel = null)
    {
        if (Server != null)
            return;

        if (channel != null)
            ServerConfig.Channel = channel;

        Server = new RemotingServer(ServerConfig);
        Server.Error += (s, ex) =>
        {
            LastServerError = ex;
            ServerErrorCount++;
        };

        try
        {
            Server.Start();    
        }
        catch (System.Net.Sockets.SocketException e) when (e.Message == "Address already in use")
        {
            Interlocked.Increment(ref _nextNetworkPort);
            ServerConfig.NetworkPort = _nextNetworkPort;
            Server.Start();
        }
    }

    public RemotingServer Server { get; private set;  }

    public ServerConfig ServerConfig { get; set; }

    public TestService TestService { get; }

    public void Dispose()
    {
        if (Server != null)
        {
            Server.Dispose();
        }

        // Ensure no default client/server state leaks to other tests
        try
        {
            RemotingClient.DefaultRemotingClient?.Dispose();
            RemotingClient.DefaultRemotingClient = null;
        }
        catch
        {
            // ignore
        }

        try
        {
            RemotingConfiguration.ShutdownAll();
        }
        catch
        {
            // ignore
        }

        if (ServerErrorCount > 0 && LastServerError != null)
        {
            Console.WriteLine($"ServerFixture.Dispose(): LastServerError: {LastServerError.ToString()}");
        }
    }
}

[CollectionDefinition("CoreRemoting")]
public class ServerCollection : ICollectionFixture<ServerFixture>
{
}
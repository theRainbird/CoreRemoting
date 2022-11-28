using System;
using System.Threading;
using CoreRemoting.ClassicRemotingApi;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using Xunit.Abstractions;

namespace CoreRemoting.Tests
{
    using Xunit;
    using CoreRemoting;
    
    public class ReturnAsProxyTests
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public ReturnAsProxyTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }
        
        [Fact]
        public void Call_on_Proxy_should_be_invoked_on_remote_service()
        {
            var factoryService = new FactoryService();
            
            var serverConfig =
                new ServerConfig()
                {
                    NetworkPort = 9084,
                    RegisterServicesAction = container =>
                    {
                        container.RegisterService<IFactoryService>(
                            factoryDelegate: () => factoryService,
                            lifetime: ServiceLifetime.Singleton);
                    },
                    IsDefault = true
                };

            using var server = new RemotingServer(serverConfig);
            server.Start();

            void ClientAction()
            {
                try
                {
                    using var client = new RemotingClient(new ClientConfig()
                    {
                        ConnectionTimeout = 0, 
                        ServerPort = 9084
                    });

                    client.Connect();

                    var factoryServiceProxy = client.CreateProxy<IFactoryService>();
                    var testServiceProxy = factoryServiceProxy.GetTestService();

                    Assert.True(RemotingServices.IsTransparentProxy(testServiceProxy));
                }
                catch (Exception e)
                {
                    _testOutputHelper.WriteLine(e.ToString());
                    throw;
                }
            }

            var clientThread = new Thread(ClientAction);
            clientThread.Start();
            clientThread.Join();
        }
    }
}
using System.Threading;
using CoreRemoting.ClassicRemotingApi;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using NUnit.Framework;

namespace CoreRemoting.Tests
{
    public class CallContextTests
    {
        [SetUp]
        public void Init()
        {
            RemotingConfiguration.DisableClassicRemotingApi();
        }

        [Test]
        [NonParallelizable]
        public void CallContext_should_flow_from_client_to_server_and_back()
        {
            var testService = 
                new TestService
                {
                    TestMethodFake = _ =>
                    {
                        CallContext.SetData("test", "Changed");
                        return CallContext.GetData("test");
                    }
                };

            var serverConfig =
                new ServerConfig()
                {
                    NetworkPort = 9093,
                    RegisterServicesAction = container =>
                        container.RegisterService<ITestService>(
                            factoryDelegate: () => testService,
                            lifetime: ServiceLifetime.Singleton)
                };

            using var server = new RemotingServer(serverConfig);
            server.Start();

            var clientThread =
                new Thread(() =>
                {
                    CallContext.SetData("test", "CallContext");

                    var client =
                        new RemotingClient(new ClientConfig()
                        {
                            ServerPort = 9093
                        });

                    client.Connect();

                    var localCallContextValueBeforeRpc = CallContext.GetData("test");
                    
                    var proxy = client.CreateProxy<ITestService>();
                    var result = (string) proxy.TestMethod("x");

                    var localCallContextValueAfterRpc = CallContext.GetData("test");
                    
                    Assert.AreNotEqual(localCallContextValueBeforeRpc, result);
                    Assert.AreEqual("Changed", result);
                    Assert.AreEqual("Changed", localCallContextValueAfterRpc);

                    client.Dispose();
                });
            
            clientThread.Start();
            clientThread.Join();
        }
    }
}
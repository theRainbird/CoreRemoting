using System.Threading;
using CoreRemoting.ClassicRemotingApi;
using CoreRemoting.Tests.Tools;
using NUnit.Framework;

namespace CoreRemoting.Tests
{
    public class RemotingServicesTests
    {
        [Test]
        public void IsOneWay_should_return_true_if_provided_method_is_OneWay()
        {
            var serviceType = typeof(ITestService);
            var isOneWay = RemotingServices.IsOneWay(serviceType.GetMethod("OneWayMethod"));
            
            Assert.IsTrue(isOneWay);
            
            isOneWay = RemotingServices.IsOneWay(serviceType.GetMethod("TestMethod"));
            
            Assert.IsFalse(isOneWay);
        }

        [Test]
        public void IsTransparentProxy_should_return_true_if_the_provided_object_is_a_proxy()
        {
            var client = new RemotingClient(
                new ClientConfig()
                {
                    ServerPort = 9099,
                    ServerHostName = "localhost"
                });
            
            var proxy = client.CreateProxy<ITestService>();

            var isProxy = RemotingServices.IsTransparentProxy(proxy);
            
            Assert.IsTrue(isProxy);

            var service = new TestService();
            isProxy = RemotingServices.IsTransparentProxy(service);
            
            Assert.IsFalse(isProxy);
        }

        [Test]
        public void Marshal_should_register_a_service_instance()
        {
            var testService = new TestService();

            using var server = new RemotingServer();
            server.Start();            
            
            string serviceName =
                RemotingServices.Marshal(testService, "test", typeof(ITestService), server.UniqueServerInstanceName);

            var registeredServiceInstance = server.ServiceRegistry.GetService(serviceName);

            Assert.AreSame(testService, registeredServiceInstance);
            Assert.IsTrue(registeredServiceInstance is ITestService);
        }

        [Test]
        public void Connect_should_create_a_proxy_for_a_remote_service()
        {
            var testService = 
                new TestService 
                {
                    TestMethodFake = arg => 
                        arg
                };

            var server = new RemotingServer(new ServerConfig { NetworkPort = 9099 });
            RemotingServices.Marshal(testService, "test", typeof(ITestService));
            server.Start();

            var clientThread = new Thread(() =>
            {
                DefaultRemotingInfrastructure.DefaultClientConfig = new ClientConfig { ServerPort = 9099 };
                var proxy = 
                    RemotingServices.Connect(
                        typeof(ITestService),
                        "test",
                        DefaultRemotingInfrastructure.DefaultClientConfig);
                
                Assert.IsTrue(RemotingServices.IsTransparentProxy(proxy));

                object result = ((ITestService) proxy).TestMethod(1);
                
                Assert.AreEqual(1, result);
            });
            
            clientThread.Start();
            clientThread.Join();
        }
    }
}
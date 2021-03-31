using System;
using System.Threading;
using CoreRemoting.ClassicRemotingApi;
using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests
{
    public class RemotingServicesTests
    {
        [Fact]
        public void IsOneWay_should_return_true_if_provided_method_is_OneWay()
        {
            var serviceType = typeof(ITestService);
            var isOneWay = RemotingServices.IsOneWay(serviceType.GetMethod("OneWayMethod"));
            
            Assert.True(isOneWay);
            
            isOneWay = RemotingServices.IsOneWay(serviceType.GetMethod("TestMethod"));
            
            Assert.False(isOneWay);
        }

        [Fact]
        public void IsTransparentProxy_should_return_true_if_the_provided_object_is_a_proxy()
        {
            var client = new RemotingClient(
                new ClientConfig()
                {
                    ServerPort = 9199,
                    ServerHostName = "localhost"
                });
            
            var proxy = client.CreateProxy<ITestService>();

            var isProxy = RemotingServices.IsTransparentProxy(proxy);
            
            Assert.True(isProxy);

            var service = new TestService();
            isProxy = RemotingServices.IsTransparentProxy(service);
            
            Assert.False(isProxy);
        }

        [Fact]
        public void Marshal_should_register_a_service_instance()
        {
            var testService = new TestService();

            using var server = new RemotingServer();
            server.Start();            
            
            string serviceName =
                RemotingServices.Marshal(testService, "test", typeof(ITestService), server.UniqueServerInstanceName);

            var registeredServiceInstance = server.ServiceRegistry.GetService(serviceName);

            Assert.Same(testService, registeredServiceInstance);
            Assert.True(registeredServiceInstance is ITestService);
        }

        [Fact]
        public void Connect_should_create_a_proxy_for_a_remote_service()
        {
            var testService = 
                new TestService 
                {
                    TestMethodFake = arg => 
                        arg
                };

            using var server = 
                new RemotingServer(new ServerConfig
                {
                    NetworkPort = 9199, 
                    IsDefault = true
                });
            
            RemotingServices.Marshal(testService, "test", typeof(ITestService));
            server.Start();

            var clientThread = new Thread(() =>
            {
                // ReSharper disable once ObjectCreationAsStatement
                new RemotingClient(new ClientConfig {ServerPort = 9199, IsDefault = true});
                
                var proxy = 
                    RemotingServices.Connect(
                        typeof(ITestService),
                        "test",
                        string.Empty);
                
                Assert.True(RemotingServices.IsTransparentProxy(proxy));

                object result = ((ITestService) proxy).TestMethod(1);
                
                RemotingClient.DefaultRemotingClient.Dispose();
                
                Assert.Equal(1, Convert.ToInt32(result));
            });
            
            clientThread.Start();
            clientThread.Join();
            
            server.Dispose();
        }
    }
}
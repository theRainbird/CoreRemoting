using System;
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
            var client = new RemotingClient(new ClientConfig()
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
                RemotingServices.Marshal(testService, typeof(ITestService), server.UniqueServerInstanceName);

            var registeredServiceInstance = server.ServiceRegistry.GetService(serviceName);

            Assert.AreSame(testService, registeredServiceInstance);
            Assert.IsTrue(registeredServiceInstance is ITestService);
        }
    }
}
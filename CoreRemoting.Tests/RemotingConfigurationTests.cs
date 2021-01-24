using System.IO;
using System.Reflection;
using CoreRemoting.Channels.Websocket;
using CoreRemoting.ClassicRemotingApi;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Serialization.Binary;
using CoreRemoting.Tests.Tools;
using NUnit.Framework;

namespace CoreRemoting.Tests
{
    public class RemotingConfigurationTests
    {
        [SetUp]
        public void Init()
        {
            RemotingConfiguration.EnableClassicRemotingApi();
        }

        [Test]
        [NonParallelizable]
        public void RegisterWellKnownServiceType_should_register_type_resolved_at_runtime()
        {
            using var server = new RemotingServer(new ServerConfig { UniqueServerInstanceName = "Server1" });
            
            RemotingConfiguration.RegisterWellKnownServiceType(
                interfaceType: typeof(ITestService),
                implementationType: typeof(TestService),
                lifetime: ServiceLifetime.Singleton,
                serviceName: "Service1",
                uniqueServerInstanceName: "Server1");

            var service = server.ServiceRegistry.GetService("Service1");
            
            Assert.IsNotNull(service);
            Assert.AreEqual(typeof(TestService), service.GetType());
            
            RemotingConfiguration.ShutdownAll();
        }
        
        [Test]
        [NonParallelizable]
        public void RemotingServer_should_register_on_construction_AND_unregister_on_Dispose()
        {
            var server = new RemotingServer(new ServerConfig()
            {
                UniqueServerInstanceName = "TestServer"
            });
            
            Assert.IsNotNull(RemotingConfiguration.GetRegisteredServer("TestServer"));
            
            server.Dispose();
            
            Assert.IsNull(RemotingConfiguration.GetRegisteredServer("TestServer"));
        }

        [Test]
        [NonParallelizable]
        public void RemotingConfiguration_Configure_should_configure_a_server()
        {
            // See TestConfig.xml to check test configuration
            var configFileName =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                    "TestConfig.xml");
            
            RemotingConfiguration.Configure(configFileName);

            var server = RemotingConfiguration.GetRegisteredServer("TestServer");
            
            Assert.IsNotNull(server);
            Assert.AreEqual(8080, server.Config.NetworkPort);
            Assert.AreEqual("test", server.Config.HostName);
            Assert.AreEqual(2048, server.Config.KeySize);
            Assert.IsInstanceOf<BinarySerializerAdapter>(server.Serializer);
            Assert.IsInstanceOf<WebsocketServerChannel>(server.Config.Channel);
            Assert.IsInstanceOf<FakeAuthProvider>(server.Config.AuthenticationProvider);
            Assert.AreEqual(true, server.Config.AuthenticationRequired);
            Assert.IsNotNull(server.ServiceRegistry.GetService("TestService"));
            Assert.IsInstanceOf<TestService>(server.ServiceRegistry.GetService("TestService"));
            
            RemotingConfiguration.ShutdownAll();
        }
    }
}
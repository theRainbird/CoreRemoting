using System.IO;
using System.Reflection;
using CoreRemoting.Channels.Websocket;
using CoreRemoting.ClassicRemotingApi;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Serialization.Binary;
using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests
{
    public class RemotingConfigurationTests
    {
        [Fact]
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
            
            Assert.NotNull(service);
            Assert.Equal(typeof(TestService), service.GetType());
            
            RemotingConfiguration.ShutdownAll();
        }
        
        [Fact]
        public void RemotingServer_should_register_on_construction_AND_unregister_on_Dispose()
        {
            using var server = new RemotingServer(new ServerConfig()
            {
                UniqueServerInstanceName = "TestServer"
            });
            
            Assert.NotNull(RemotingConfiguration.GetRegisteredServer("TestServer"));
            
            server.Dispose();
            
            Assert.Null(RemotingConfiguration.GetRegisteredServer("TestServer"));
        }

        [Fact]
        public void RemotingConfiguration_Configure_should_configure_a_server()
        {
            // See TestConfig.xml to check test configuration
            var configFileName =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                    "TestConfig.xml");
            
            RemotingConfiguration.Configure(configFileName);

            using var server = RemotingConfiguration.GetRegisteredServer("TestServer4711");
            
            Assert.NotNull(server);
            Assert.Equal(8080, server.Config.NetworkPort);
            Assert.Equal("test", server.Config.HostName);
            Assert.Equal(2048, server.Config.KeySize);
            Assert.IsType<BinarySerializerAdapter>(server.Serializer);
            Assert.IsType<WebsocketServerChannel>(server.Config.Channel);
            Assert.IsType<FakeAuthProvider>(server.Config.AuthenticationProvider);
            Assert.True(server.Config.AuthenticationRequired);
            Assert.NotNull(server.ServiceRegistry.GetService("TestService"));
            Assert.IsType<TestService>(server.ServiceRegistry.GetService("TestService"));
            
            RemotingConfiguration.ShutdownAll();
        }
    }
}
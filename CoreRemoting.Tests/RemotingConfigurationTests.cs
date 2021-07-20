using System;
using System.IO;
using System.Reflection;
using CoreRemoting.Authentication;
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
        public void RemotingConfiguration_Configure_should_configure_a_server_and_a_client()
        {
            // See TestConfig.xml to check test configuration
            var configFileName =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                    "TestConfig.xml");
            
            RemotingConfiguration.Configure(
                fileName: configFileName,
                credentials: new []
                {
                    new Credential() { Name = "token", Value = "123" }
                });

            var server = RemotingConfiguration.GetRegisteredServer("TestServer4711");

            if (!server.Config.Channel.IsListening)
                throw new ApplicationException("Channel is listening.");
            
            var authProvider = (FakeAuthProvider) server.Config.AuthenticationProvider;
            authProvider.AuthenticateFake = credentials => 
                credentials.Length == 1 && 
                credentials[0].Name == "token" &&
                credentials[0].Value == "123";
            
            Assert.NotNull(server);
            Assert.Equal(8089, server.Config.NetworkPort);
            Assert.Equal("localhost", server.Config.HostName);
            Assert.Equal(2048, server.Config.KeySize);
            Assert.IsType<BinarySerializerAdapter>(server.Serializer);
            Assert.IsType<WebsocketServerChannel>(server.Config.Channel);
            Assert.IsType<FakeAuthProvider>(server.Config.AuthenticationProvider);
            Assert.True(server.Config.AuthenticationRequired);
            Assert.True(server.Config.MessageEncryption);
            Assert.NotNull(server.ServiceRegistry.GetService("TestService"));
            Assert.IsType<TestService>(server.ServiceRegistry.GetService("TestService"));

            var client = RemotingConfiguration.GetRegisteredClient("TestClient");
        
            Assert.NotNull(client);
            Assert.Equal(8089, client.Config.ServerPort);
            Assert.Equal("localhost", client.Config.ServerHostName);
            Assert.Equal(2048, client.Config.KeySize);
            Assert.IsType<BinarySerializerAdapter>(client.Config.Serializer);
            Assert.IsType<WebsocketClientChannel>(client.Config.Channel);
            Assert.True(client.Config.MessageEncryption);
            Assert.True(client.Config.IsDefault);
            Assert.Equal(200, client.Config.ConnectionTimeout);
            Assert.Equal(110, client.Config.AuthenticationTimeout);
            Assert.Equal(30000, client.Config.InvocationTimeout);

            var proxy = (ITestService)RemotingServices.Connect(typeof(ITestService), "TestService");
            Assert.True(client.IsConnected);
            
            var result = proxy.Echo("hello");
            Assert.Equal("hello", result);

            RemotingConfiguration.ShutdownAll();
        }
    }
}
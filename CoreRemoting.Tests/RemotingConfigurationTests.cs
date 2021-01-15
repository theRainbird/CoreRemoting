using System;
using System.Data;
using CoreRemoting.ClassicRemotingApi;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using NUnit.Framework;

namespace CoreRemoting.Tests
{
    public class RemotingConfigurationTests
    {
        [Test]
        public void RegisterWellKnownServiceType_should_register_type_resolved_at_runtime()
        {
            using var server = new RemotingServer();
            
            RemotingConfiguration.RegisterWellKnownServiceType(
                typeof(ITestService),
                typeof(TestService),
                ServiceLifetime.Singleton,
                "TestService");

            var service = server.ServiceRegistry.GetService("TestService");
            
            Assert.IsNotNull(service);
            Assert.AreEqual(typeof(TestService), service.GetType());
        }

        [Test]
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
    }
}
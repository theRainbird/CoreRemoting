using System;
using System.Threading;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using CoreRemoting.Toolbox;
using Xunit;

namespace CoreRemoting.Tests;

public class RemotingServerLifecycleTests
{
    private static int _nextPort = 9199;

    [Fact]
    public void Stop_then_Dispose_should_not_crash()
    {
        var port = Interlocked.Increment(ref _nextPort);

        var server = new RemotingServer(new ServerConfig
        {
            MessageEncryption = false,
            NetworkPort = port,
            RegisterServicesAction = container =>
            {
                container.RegisterService<ITestService, TestService>(
                    lifetime: ServiceLifetime.Singleton);
            }
        });

        try
        {
            server.Start();

            using var client = new RemotingClient(new ClientConfig
            {
                ConnectionTimeout = 0,
                MessageEncryption = false,
                ServerPort = port,
            });

            client.Connect();

            var proxy = client.CreateProxy<ITestService>();
            var result = proxy.Echo("hello");
            Assert.Equal("hello", result);

            server.Stop();
        }
        finally
        {
            server.Dispose();
        }
    }
}

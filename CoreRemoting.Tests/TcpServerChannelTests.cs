using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Tcp;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests;

public class TcpServerChannelTests
{
    private static int _nextPort = 9399;

    private static RemotingServer StartServer(IServerChannel serverChannel, int port)
    {
        var server = new RemotingServer(new ServerConfig
        {
            Channel = serverChannel,
            NetworkPort = port,
            MessageEncryption = false,
            RegisterServicesAction = container =>
                container.RegisterService<ITestService, TestService>(ServiceLifetime.Singleton)
        });
        server.Start();
        return server;
    }

    private static IPAddress[] GetListenerAddresses(int port) =>
        IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Where(endPoint => endPoint.Port == port)
            .Select(endPoint => endPoint.Address)
            .ToArray();

    [Fact]
    public void Server_channel_with_loopback_address_should_listen_only_on_loopback_and_serve_local_clients()
    {
        var port = Interlocked.Increment(ref _nextPort);

        using var server = StartServer(new TcpServerChannel("127.0.0.1"), port);

        Assert.Equal([IPAddress.Loopback], GetListenerAddresses(port));

        using var client = new RemotingClient(new ClientConfig
        {
            Channel = new TcpClientChannel(),
            ServerHostName = "localhost",
            ServerPort = port,
            MessageEncryption = false,
            ConnectionTimeout = 0
        });
        client.Connect();

        Assert.Equal("hello", client.CreateProxy<ITestService>().Echo("hello"));
    }

    [Fact]
    public void Server_channel_without_address_should_listen_on_all_network_interfaces()
    {
        var port = Interlocked.Increment(ref _nextPort);

        using var server = StartServer(new TcpServerChannel(), port);

        Assert.Equal([IPAddress.Any], GetListenerAddresses(port));
    }
}

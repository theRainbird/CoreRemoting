using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using CoreRemoting;
using CoreRemoting.Channels;
using CoreRemoting.Channels.NamedPipe;
using CoreRemoting.Channels.Null;
using CoreRemoting.Channels.Tcp;
using CoreRemoting.Channels.Websocket;

namespace CoreRemoting.Benchmark;

public enum RpcChannelScenario
{
    Null,
    NamedPipe,
    Websocket_NoEncryption,
    Websocket_Encryption,
    Tcp_NoEncryption,
    Tcp_Encryption
}

[MemoryDiagnoser]
public class RpcBenchmark
{
    private RemotingServer _server = null!;
    private RemotingClient _client = null!;
    private ITestService _proxy = null!;
    private ClientConfig _clientConfig = null!;
    private RemotingClient _connectClient = null!;

    [Params(
        RpcChannelScenario.Null,
        RpcChannelScenario.NamedPipe,
        RpcChannelScenario.Websocket_NoEncryption,
        RpcChannelScenario.Websocket_Encryption,
        RpcChannelScenario.Tcp_NoEncryption,
        RpcChannelScenario.Tcp_Encryption
    )]
    public RpcChannelScenario Scenario { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var (serverChannel, clientChannel, encryption) = CreateChannelsForScenario(Scenario);

        var serverConfig = new ServerConfig
        {
            Channel = serverChannel,
            HostName = "localhost",
            MessageEncryption = encryption,
            KeySize = 1024,
            RegisterServicesAction = container =>
                container.RegisterService<ITestService, TestService>()
        };

        _server = new RemotingServer(serverConfig);
        _server.Start();

        _clientConfig = new ClientConfig
        {
            Channel = clientChannel,
            ServerHostName = "localhost",
            ServerPort = _server.Config.NetworkPort,
            MessageEncryption = encryption,
            KeySize = 1024
        };

        _client = new RemotingClient(_clientConfig);
        _client.Connect();
        _proxy = _client.CreateProxy<ITestService>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _server?.Stop();
        _server?.Dispose();
    }

    [IterationSetup]
    public void ConnectSetup()
    {
        _connectClient = new RemotingClient(_clientConfig);
    }

    [IterationCleanup]
    public void ConnectCleanup()
    {
        _connectClient?.Dispose();
    }

    [Benchmark]
    public async Task ConnectAsync()
    {
        await _connectClient.ConnectAsync();
    }

    [Benchmark]
    public string EchoCall() => _proxy.Echo("Hello");

    [Benchmark]
    public int GetCallCount() => _proxy.GetCallCount();

    [Benchmark]
    public void FireEvent() => _proxy.FireServiceEvent();

    private static (IServerChannel server, IClientChannel client, bool encryption) CreateChannelsForScenario(RpcChannelScenario scenario)
    {
        switch (scenario)
        {
            case RpcChannelScenario.Null:
                return (new NullServerChannel(), new NullClientChannel(), false);

            case RpcChannelScenario.NamedPipe:
                return (new NamedPipeServerChannel(), new NamedPipeClientChannel(), false);

            case RpcChannelScenario.Websocket_NoEncryption:
                return (new WebsocketServerChannel(), new WebsocketClientChannel(), false);

            case RpcChannelScenario.Websocket_Encryption:
                return (new WebsocketServerChannel(), new WebsocketClientChannel(), true);

            case RpcChannelScenario.Tcp_NoEncryption:
                return (new TcpServerChannel(), new TcpClientChannel(), false);

            case RpcChannelScenario.Tcp_Encryption:
                return (new TcpServerChannel(), new TcpClientChannel(), true);

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }
    }
}

public interface ITestService
{
    string Echo(string message);
    int GetCallCount();
    event Action? ServiceEvent;
    void FireServiceEvent();
}

public class TestService : ITestService
{
    private int _callCount;

    public int GetCallCount() => _callCount;

    public event Action? ServiceEvent;

    public string Echo(string message)
    {
        Interlocked.Increment(ref _callCount);
        return message;
    }

    public void FireServiceEvent() => ServiceEvent?.Invoke();
}

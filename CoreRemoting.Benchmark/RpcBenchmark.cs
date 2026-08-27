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
using CoreRemoting.Encryption;

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
    private RemotingServer _mainServer = null!;
    private RemotingClient _mainClient = null!;
    private ITestService _proxy = null!;

    private RemotingServer _connectServer = null!;

    // Параметры для ConnectAsync (вместо готового ClientConfig)
    private bool _encryption;
    private string? _connectPipeName;

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
        // Main server channels
        var (mainServerChannel, mainClientChannel, encryption) = CreateChannelsForScenario(Scenario);
        _encryption = encryption;

        var mainPipeName = Scenario == RpcChannelScenario.NamedPipe ? "MainPipe" : null;
        _mainServer = new RemotingServer(new ServerConfig
        {
            Channel = mainServerChannel,
            NetworkPort = 9192,
            HostName = "localhost",
            MessageEncryption = encryption,
            KeySize = 512,
            ChannelConnectionName = mainPipeName,
            RegisterServicesAction = c => c.RegisterService<ITestService, TestService>()
        });
        _mainServer.Start();

        _mainClient = new RemotingClient(new ClientConfig
        {
            Channel = mainClientChannel,
            ServerHostName = "localhost",
            ServerPort = 9192,
            MessageEncryption = encryption,
            KeySize = 512,
            ChannelConnectionName = mainPipeName
        });
        _mainClient.Connect();
        _proxy = _mainClient.CreateProxy<ITestService>();

        // Connect server — отдельный инстанс канала
        var (connectServerChannel, _, _) = CreateChannelsForScenario(Scenario);
        _connectPipeName = Scenario == RpcChannelScenario.NamedPipe ? "ConnectPipe" : null;
        _connectServer = new RemotingServer(new ServerConfig
        {
            Channel = connectServerChannel,
            NetworkPort = 9193,
            HostName = "localhost",
            MessageEncryption = encryption,
            KeySize = 512,
            ChannelConnectionName = _connectPipeName
        });
        _connectServer.Start();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _mainClient?.Dispose();
        _mainServer?.Stop();
        _mainServer?.Dispose();

        _connectServer?.Stop();
        _connectServer?.Dispose();
    }

    [Benchmark]
    public async Task ConnectAsync()
    {
        // Создаём НОВЫЙ канал и конфиг для каждой итерации
        var (_, clientChannel, _) = CreateChannelsForScenario(Scenario);
        var config = new ClientConfig
        {
            Channel = clientChannel,
            ServerHostName = "localhost",
            ServerPort = 9193,
            MessageEncryption = _encryption,
            KeySize = 512,
            ChannelConnectionName = _connectPipeName,
        };

        using var client = new RemotingClient(config);
        await client.ConnectAsync();
    }

    [Benchmark]
    public string EchoCall() => _proxy.Echo("Hello");

    [Benchmark]
    public int GetCallCount() => _proxy.GetCallCount();

    [Benchmark]
    public void FireEvent() => _proxy.FireServiceEvent();

    private static (IServerChannel server, IClientChannel client, bool encryption)
        CreateChannelsForScenario(RpcChannelScenario scenario) => scenario switch
    {
        RpcChannelScenario.Null => (new NullServerChannel(), new NullClientChannel(), false),
        RpcChannelScenario.NamedPipe => (new NamedPipeServerChannel(), new NamedPipeClientChannel(), false),
        RpcChannelScenario.Websocket_NoEncryption => (new WebsocketServerChannel(), new WebsocketClientChannel(), false),
        RpcChannelScenario.Websocket_Encryption => (new WebsocketServerChannel(), new WebsocketClientChannel(), true),
        RpcChannelScenario.Tcp_NoEncryption => (new TcpServerChannel(), new TcpClientChannel(), false),
        RpcChannelScenario.Tcp_Encryption => (new TcpServerChannel(), new TcpClientChannel(), true),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
    };
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

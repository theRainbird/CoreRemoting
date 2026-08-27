using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using Perfolizer.Horology;
using Perfolizer.Metrology;
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
    Ws_Plain,
    Ws_Encr,
    Tcp_Plain,
    Tcp_Encr
}

[MemoryDiagnoser]
[Config(typeof(Config))]
public class RpcBenchmark
{
    private class Config : ManualConfig
    {
        public Config()
        {
            HideColumns(Column.StdDev, Column.Error);

            SummaryStyle = SummaryStyle.Default
                .WithTimeUnit(TimeUnit.Microsecond)
                .WithSizeUnit(SizeUnit.KB);
        }
    }

    private RemotingServer _mainServer = null!;
    private RemotingClient _mainClient = null!;
    private ITestService _proxy = null!;

    private RemotingServer _connectServer = null!;
    private bool _encryption;
    private string? _connectPipeName;

    [Params(
        RpcChannelScenario.Null,
        RpcChannelScenario.NamedPipe,
        RpcChannelScenario.Ws_Plain,
        RpcChannelScenario.Ws_Encr,
        RpcChannelScenario.Tcp_Plain,
        RpcChannelScenario.Tcp_Encr
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

        // Separate server for Connect benchmark
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
    public void Connect()
    {
        // Create a new channel and client config per iteration
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
        client.Connect();
    }

    [Benchmark]
    public string EchoCall() => _proxy.Echo("Hello");

    [Benchmark]
    public int CallCount() => _proxy.CallCount;

    [Benchmark]
    public void FireEvent() => _proxy.FireServiceEvent();

    private static (IServerChannel server, IClientChannel client, bool encryption)
        CreateChannelsForScenario(RpcChannelScenario scenario) => scenario switch
    {
        RpcChannelScenario.Null => (new NullServerChannel(), new NullClientChannel(), false),
        RpcChannelScenario.NamedPipe => (new NamedPipeServerChannel(), new NamedPipeClientChannel(), false),
        RpcChannelScenario.Ws_Plain => (new WebsocketServerChannel(), new WebsocketClientChannel(), false),
        RpcChannelScenario.Ws_Encr => (new WebsocketServerChannel(), new WebsocketClientChannel(), true),
        RpcChannelScenario.Tcp_Plain => (new TcpServerChannel(), new TcpClientChannel(), false),
        RpcChannelScenario.Tcp_Encr => (new TcpServerChannel(), new TcpClientChannel(), true),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
    };
}

public interface ITestService
{
    string Echo(string message);
    int CallCount { get; }
    event Action? ServiceEvent;
    void FireServiceEvent();
}

public class TestService : ITestService
{
    private int _callCount;

    public int CallCount => _callCount;

    public event Action? ServiceEvent;

    public string Echo(string message)
    {
        Interlocked.Increment(ref _callCount);
        return message;
    }

    public void FireServiceEvent() => ServiceEvent?.Invoke();
}

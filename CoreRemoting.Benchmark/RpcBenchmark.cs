using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using CoreRemoting.Channels;
using CoreRemoting.Channels.NamedPipe;
using CoreRemoting.Channels.Null;
using CoreRemoting.Channels.Tcp;
using CoreRemoting.Channels.Websocket;

#if NET9_0_OR_GREATER
using CoreRemoting.Channels.Quic;
#endif

using Perfolizer.Horology;
using Perfolizer.Metrology;

namespace CoreRemoting.Benchmark;

public enum RpcChannel
{
    Null,
    NamedPipe,
    QuicPlain,
    QuicEncr,
    WsockPlain,
    WsockEncr,
    TcpPlain,
    TcpEncr
}

[MemoryDiagnoser]
[Config(typeof(Config))]
public class RpcBenchmark
{
    private class Config : ManualConfig
    {
        public Config()
        {
            AddJob(
                Job.Default
            //    Job.ShortRun
            //        .WithWarmupCount(2)
            //        .WithIterationCount(5)
            //        .WithId("Fast")
            );

            HideColumns(Column.StdDev, Column.Error);

            SummaryStyle = SummaryStyle.Default
                .WithTimeUnit(TimeUnit.Microsecond)
                .WithSizeUnit(SizeUnit.KB);
        }
    }

    private RemotingServer _server = null!;
    private RemotingClient _mainClient = null!;
    private ITestService _proxy = null!;

    private bool _encryption;
    private string? _pipeName;

    [Params(
        RpcChannel.Null,
        RpcChannel.NamedPipe,
        RpcChannel.WsockPlain,
        RpcChannel.WsockEncr,
#if NET9_0_OR_GREATER
        RpcChannel.QuicPlain,
        RpcChannel.QuicEncr,
#endif
        RpcChannel.TcpPlain,
        RpcChannel.TcpEncr
    )]
    public RpcChannel Channel { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var serverChannel = CreateServerChannel(Channel);
        var clientChannel = CreateClientChannel(Channel);
        _encryption = IsEncryptionEnabled(Channel);
        _pipeName = Channel == RpcChannel.NamedPipe ? "BenchmarkPipe" : null;

        _server = new RemotingServer(new ServerConfig
        {
            Channel = serverChannel,
            NetworkPort = 9192,
            HostName = "localhost",
            MessageEncryption = _encryption,
            KeySize = 512,
            ChannelConnectionName = _pipeName,
            RegisterServicesAction = c => c.RegisterService<ITestService, TestService>()
        });
        _server.Start();

        _mainClient = new RemotingClient(new ClientConfig
        {
            Channel = clientChannel,
            ServerHostName = "localhost",
            ServerPort = 9192,
            MessageEncryption = _encryption,
            KeySize = 512,
            ChannelConnectionName = _pipeName
        });
        _mainClient.Connect();
        _proxy = _mainClient.CreateProxy<ITestService>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _mainClient?.Dispose();
        _server?.Stop();
        _server?.Dispose();
    }

    [Benchmark]
    public void Connect()
    {
        var config = new ClientConfig
        {
            Channel = CreateClientChannel(Channel),
            ServerHostName = "localhost",
            ServerPort = 9192,
            MessageEncryption = _encryption,
            KeySize = 512,
            ChannelConnectionName = _pipeName,
        };

        using var client = new RemotingClient(config);
        client.Connect();
    }

    [Benchmark]
    public string Method() => _proxy.Echo("Hello");

    [Benchmark]
    public int Property() => _proxy.CallCount;

    [Benchmark]
    public void FireEvent() => _proxy.FireServiceEvent();

    private static IServerChannel CreateServerChannel(RpcChannel scenario) => scenario switch
    {
        RpcChannel.Null => new NullServerChannel(),
        RpcChannel.NamedPipe => new NamedPipeServerChannel(),
#if NET9_0_OR_GREATER
        RpcChannel.QuicPlain or RpcChannel.QuicEncr => new QuicServerChannel(),
#endif
        RpcChannel.WsockPlain or RpcChannel.WsockEncr => new WebsocketServerChannel(),
        RpcChannel.TcpPlain or RpcChannel.TcpEncr => new TcpServerChannel(),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
    };

    private static IClientChannel CreateClientChannel(RpcChannel scenario) => scenario switch
    {
        RpcChannel.Null => new NullClientChannel(),
        RpcChannel.NamedPipe => new NamedPipeClientChannel(),
#if NET9_0_OR_GREATER
        RpcChannel.QuicPlain or RpcChannel.QuicEncr => new QuicClientChannel(),
#endif
        RpcChannel.WsockPlain or RpcChannel.WsockEncr => new WebsocketClientChannel(),
        RpcChannel.TcpPlain or RpcChannel.TcpEncr => new TcpClientChannel(),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
    };

    private static bool IsEncryptionEnabled(RpcChannel scenario) => scenario switch
    {
#if NET9_0_OR_GREATER
        RpcChannel.QuicEncr => true,
#endif
        RpcChannel.WsockEncr => true,
        RpcChannel.TcpEncr => true,
        _ => false
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

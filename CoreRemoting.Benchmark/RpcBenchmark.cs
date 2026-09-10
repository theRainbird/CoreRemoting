using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using Perfolizer.Horology;
using Perfolizer.Metrology;
using static CoreRemoting.Benchmark.Setup;

namespace CoreRemoting.Benchmark;

[MemoryDiagnoser]
[Config(typeof(Config))]
public class RpcBenchmark
{
    private class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.Default);

            HideColumns(Column.StdDev, Column.Error);

            SummaryStyle = SummaryStyle.Default
                .WithTimeUnit(TimeUnit.Microsecond)
                .WithSizeUnit(SizeUnit.KB);
        }
    }

    private RemotingServer _server = null!;
    private RemotingClient _mainClient = null!;
    private ITestService _proxy = null!;

    [ParamsSource(typeof(Setup), nameof(Scenarios))]
    public ICase Setup { get; set; } = null!;

    [GlobalSetup]
    public void SetupServer()
    {
        _server = new RemotingServer(new ServerConfig
        {
            Channel = Setup.CreateServer(),
            NetworkPort = 9192,
            HostName = "localhost",
            MessageEncryption = Setup.Encrypted,
            KeySize = 512,
            ChannelConnectionName = Setup.ConnectionName,
            RegisterServicesAction = c => c.RegisterService<ITestService, TestService>()
        });
        _server.Start();

        _mainClient = new RemotingClient(new ClientConfig
        {
            Channel = Setup.CreateClient(),
            ServerHostName = "localhost",
            ServerPort = 9192,
            MessageEncryption = Setup.Encrypted,
            KeySize = 512,
            ChannelConnectionName = Setup.ConnectionName
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
            Channel = Setup.CreateClient(),
            ServerHostName = "localhost",
            ServerPort = 9192,
            MessageEncryption = Setup.Encrypted,
            KeySize = 512,
            ChannelConnectionName = Setup.ConnectionName
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

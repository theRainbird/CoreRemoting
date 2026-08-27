using System;
using System.Threading;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using CoreRemoting;
using CoreRemoting.Channels;
using CoreRemoting.Channels.NamedPipe;
using CoreRemoting.Channels.Null;
using CoreRemoting.Channels.Tcp;
using CoreRemoting.Channels.Websocket;

namespace CoreRemoting.Benchmark;

/// <summary>
/// Base class for RPC benchmarks.
/// </summary>
public abstract class RpcBenchmarkBase
{
    protected RemotingServer Server = null!;
    protected RemotingClient Client = null!;
    protected ITestService Proxy = null!;

    /// <summary>
    /// Encryption flag. Can be overridden in derived classes with [Params].
    /// </summary>
    public virtual bool Encryption { get; set; }

    /// <summary>
    /// Override to indicate whether the channel supports encryption.
    /// </summary>
    protected virtual bool SupportsEncryption => true;

    protected abstract IServerChannel CreateServerChannel();

    protected abstract IClientChannel CreateClientChannel();

    [GlobalSetup]
    public void Setup()
    {
        // Force encryption off if channel does not support it.
        if (!SupportsEncryption)
            Encryption = false;

        // Configure and start the server.
        var serverConfig = new ServerConfig
        {
            Channel = CreateServerChannel(),
            MessageEncryption = Encryption,
            KeySize = 1024,
            RegisterServicesAction = container =>
                container.RegisterService<ITestService, TestService>()
        };
        Server = new RemotingServer(serverConfig);
        Server.Start();

        // Configure and connect the client.
        var clientConfig = new ClientConfig
        {
            Channel = CreateClientChannel(),
            ServerPort = Server.Config.NetworkPort,
            MessageEncryption = Encryption,
            KeySize = 1024
        };
        Client = new RemotingClient(clientConfig);
        Client.Connect();

        Proxy = Client.CreateProxy<ITestService>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Client?.Dispose();
        Server?.Stop();
        Server?.Dispose();
    }

    [Benchmark]
    public string EchoCall() => Proxy.Echo("Hello");

    [Benchmark]
    public int GetCallCount() => Proxy.GetCallCount();

    [Benchmark]
    public void FireEvent() => Proxy.FireServiceEvent();
}

// ----- Channel-specific benchmarks -----

public class NullChannelBenchmark : RpcBenchmarkBase
{
    protected override bool SupportsEncryption => false;

    protected override IServerChannel CreateServerChannel() => new NullServerChannel();
    protected override IClientChannel CreateClientChannel() => new NullClientChannel();
}

public class NamedPipeBenchmark : RpcBenchmarkBase
{
    protected override bool SupportsEncryption => false;

    protected override IServerChannel CreateServerChannel() => new NamedPipeServerChannel();
    protected override IClientChannel CreateClientChannel() => new NamedPipeClientChannel();
}

public class WebsocketBenchmark : RpcBenchmarkBase
{
    [Params(false, true)]
    public override bool Encryption { get; set; }

    protected override bool SupportsEncryption => true;

    protected override IServerChannel CreateServerChannel() => new WebsocketServerChannel();
    protected override IClientChannel CreateClientChannel() => new WebsocketClientChannel();
}

public class TcpBenchmark : RpcBenchmarkBase
{
    [Params(false, true)]
    public override bool Encryption { get; set; }

    protected override bool SupportsEncryption => true;

    protected override IServerChannel CreateServerChannel() => new TcpServerChannel();
    protected override IClientChannel CreateClientChannel() => new TcpClientChannel();
}

// ----- Test service -----

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

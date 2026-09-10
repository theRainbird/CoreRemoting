using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Channels;
using CoreRemoting.Channels.NamedPipe;
using CoreRemoting.Tests.Tools;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests;

[Collection("CoreRemoting")]
public class RpcTests_NamedPipe : RpcTests
{
    protected override IServerChannel ServerChannel => new NamedPipeServerChannel
    {
        TraceWriteLine = Console.Error.WriteLine,
    };

    protected override IClientChannel ClientChannel => new NamedPipeClientChannel
    {
        TraceWriteLine = Console.Error.WriteLine,
    };

    public RpcTests_NamedPipe(ServerFixture serverFixture, ITestOutputHelper testOutputHelper) : base(serverFixture,
        testOutputHelper)
    {
        // ChannelConnectionName now set in ConfigureServer before server starts
    }

    protected override void ConfigureServer(ServerConfig config)
    {
        base.ConfigureServer(config);
        config.ChannelConnectionName = "CoreRemoting";
    }

    [Fact]
    public void NamedPipe_Client_can_connect_and_call_remote_service()
    {
        void ClientAction()
        {
            try
            {
                var stopWatch = new Stopwatch();
                stopWatch.Start();

                using var client = new RemotingClient(new ClientConfig()
                {
                    ConnectionTimeout = 5,
                    MessageEncryption = false,
                    Channel = new NamedPipeClientChannel(),
                    ChannelConnectionName = "CoreRemoting"
                });

                stopWatch.Stop();
                _testOutputHelper.WriteLine($"Creating client took {stopWatch.ElapsedMilliseconds} ms");
                stopWatch.Reset();
                stopWatch.Start();

                client.Connect();

                stopWatch.Stop();
                _testOutputHelper.WriteLine($"Establishing connection took {stopWatch.ElapsedMilliseconds} ms");
                stopWatch.Reset();
                stopWatch.Start();

                var proxy = client.CreateProxy<ITestService>();

                stopWatch.Stop();
                _testOutputHelper.WriteLine($"Creating proxy took {stopWatch.ElapsedMilliseconds} ms");
                stopWatch.Reset();
                stopWatch.Start();

                var result = proxy.TestMethod("test");

                stopWatch.Stop();
                _testOutputHelper.WriteLine($"Remote method invocation took {stopWatch.ElapsedMilliseconds} ms");

                Assert.Equal("test", result);
            }
            catch (Exception e)
            {
                _testOutputHelper.WriteLine(e.ToString());
                throw;
            }
        }

        var clientThread = new Thread(ClientAction);
        clientThread.Start();
        clientThread.Join();

        Assert.True(_remoteServiceCalled);
        Assert.Equal(0, _serverFixture.ServerErrorCount);
    }

    [Fact]
    public void NamedPipe_Client_can_handle_different_method_calls()
    {
        void ClientAction()
        {
            try
            {
                using var client = new RemotingClient(new ClientConfig()
                {
                    ConnectionTimeout = 5,
                    MessageEncryption = false,
                    Channel = new NamedPipeClientChannel(),
                    ChannelConnectionName = "CoreRemoting"
                });

                client.Connect();
                var proxy = client.CreateProxy<ITestService>();

                // Test different method types
                var echoResult = proxy.Echo("hello");
                Assert.Equal("hello", echoResult);

                var reverseResult = proxy.Reverse("abc");
                Assert.Equal("cba", reverseResult);
            }
            catch (Exception e)
            {
                _testOutputHelper.WriteLine(e.ToString());
                throw;
            }
        }

        var clientThread = new Thread(ClientAction);
        clientThread.Start();
        clientThread.Join();

        Assert.Equal(0, _serverFixture.ServerErrorCount);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CoreRemoting.Channels.Null;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.Tests.Tools;
using CoreRemoting.Threading;
using CoreRemoting.Toolbox;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests;

[Collection("CoreRemoting")]
public class InvokerTests : IClassFixture<ServerFixture>, IDisposable
{
    private readonly ServerFixture _serverFixture;
    private readonly ITestOutputHelper _testOutputHelper;

    public InvokerTests(ServerFixture serverFixture, ITestOutputHelper testOutputHelper)
    {
        EventStub.DelegateInvoker = DelegateInvoker;

        _serverFixture = serverFixture;
        _testOutputHelper = testOutputHelper;

        _serverFixture.Start(new NullServerChannel());
    }

    protected virtual IDelegateInvoker DelegateInvoker => new SimpleDynamicInvoker();

    protected virtual bool ShouldPreserveEventOrder => true;

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(300)]
    public void HeavyweightObjectSimulator_simulates_heavy_serialization(int delay)
    {
        var sw = Stopwatch.StartNew();
        var data = JsonConvert.SerializeObject(new HeavyweightObjectSimulator
        {
            SerializationDelay = delay,
        });

        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds >= delay);
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "Not applicable")]
    public async Task Event_order_depends_on_the_delegate_invoker()
    {
        using var ctx = ValidationSyncContext.Install();
        using var client = new RemotingClient(new()
        {
            ConnectionTimeout = 0,
            SendTimeout = 0,
            MessageEncryption = false,
            Channel = new NullClientChannel(),
            ServerPort = _serverFixture.Server.Config.NetworkPort,
        });

        client.Connect();

        var proxy = client.CreateProxy<ITestService>();
        var eventCounter = new AsyncCounter();
        var lastValue = 0;
        var values = new List<int>();
        var brokenOrder = false;
        var asyncLock = new AsyncLock();

        proxy.HeavyEvent += async (s, e) =>
        {
            using (await asyncLock)
            {
                if (e.Counter < lastValue)
                {
                    brokenOrder = true;
                }

                values.Add(e.Counter);
                lastValue = e.Counter;
                eventCounter++;
            }
        };

        var expectedEventCount = proxy.FireHeavyEvents(200, 1, 100, 1, 1, 10, 1);

        await eventCounter[expectedEventCount].Timeout(2).ConfigureAwait(false);

        Console.WriteLine($"Event order preserved: {
            ShouldPreserveEventOrder} ***>>> {string.Join(", ", values)} <<<***");

        Assert.NotEqual(ShouldPreserveEventOrder, brokenOrder);
    }

    public void Dispose()
    {
        // reset the invoker to default
        EventStub.DelegateInvoker = null;
    }
}
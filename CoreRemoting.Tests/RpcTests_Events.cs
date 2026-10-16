using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Tests.Tools;
using CoreRemoting.Threading;
using CoreRemoting.Toolbox;
using Xunit;

namespace CoreRemoting.Tests;

public partial class RpcTests
{
    [Fact]
    public async Task Delegate_invoked_on_server_should_callback_client()
    {
        string argumentFromServer = null;
        var serverCalled = new TaskCompletionSource<bool>();

        void ClientAction()
        {
            using var ctx = ValidationSyncContext.Install();

            try
            {
                using var client = new RemotingClient(
                    new ClientConfig()
                    {
                        ConnectionTimeout = 0,
                        Channel = ClientChannel,
                        MessageEncryption = false,
                        ServerPort = _serverFixture.Server.Config.NetworkPort,
                    });

                client.Connect();

                var proxy = client.CreateProxy<ITestService>();
                proxy.TestMethodWithDelegateArg(arg =>
                {
                    argumentFromServer = arg;
                    serverCalled.TrySetResult(true);
                });
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

        await serverCalled.Task.Timeout(1);
        Assert.Equal("test", argumentFromServer);
        CheckServerErrorCount();
    }

    [Theory]
    [InlineData("TestService_Singleton_Service")]
    [InlineData("TestService_Singleton_Factory")]
    [InlineData("TestService_SingleCall_Service")]
    [InlineData("TestService_SingleCall_Factory")]
    [InlineData("TestService_Scoped_Service")]
    [InlineData("TestService_Scoped_Factory")]
    public virtual void Events_should_work_remotely(string serviceName)
    {
        using var ctx = ValidationSyncContext.Install();

        var serviceEventCallCount = 0;
        var customDelegateEventCallCount = 0;

        using var client = new RemotingClient(
            new ClientConfig()
            {
                ConnectionTimeout = 0,
                SendTimeout = 0,
                Channel = ClientChannel,
                MessageEncryption = false,
                ServerPort = _serverFixture.Server.Config.NetworkPort,
            });

        client.Connect();

        var proxy = client.CreateProxy<ITestService>(serviceName);

        var serviceEventResetEvent = new ManualResetEventSlim(initialState: false);
        var customDelegateEventResetEvent = new ManualResetEventSlim(initialState: false);

        proxy.ServiceEvent += () =>
        {
            Interlocked.Increment(ref serviceEventCallCount);
            serviceEventResetEvent.Set();
        };

        proxy.CustomDelegateEvent += _ =>
        {
            Interlocked.Increment(ref customDelegateEventCallCount);
            customDelegateEventResetEvent.Set();
        };

        proxy.FireServiceEvent();
        proxy.FireCustomDelegateEvent();

        serviceEventResetEvent.Wait(1000);
        customDelegateEventResetEvent.Wait(1000);

        Assert.Equal(1, serviceEventCallCount);
        Assert.Equal(1, customDelegateEventCallCount);
        CheckServerErrorCount();
    }

    [Theory]
    [InlineData("TestService_Singleton_Service", true)]
    [InlineData("TestService_Singleton_Service", false)]
    [InlineData("TestService_Singleton_Factory", true)]
    [InlineData("TestService_Singleton_Factory", false)]
    [InlineData("TestService_SingleCall_Service", true)]
    [InlineData("TestService_SingleCall_Service", false)]
    [InlineData("TestService_SingleCall_Factory", true)]
    [InlineData("TestService_SingleCall_Factory", false)]
    [InlineData("TestService_Scoped_Service", true)]
    [InlineData("TestService_Scoped_Service", false)]
    [InlineData("TestService_Scoped_Factory", true)]
    [InlineData("TestService_Scoped_Factory", false)]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task Multicast_subscription_semantics_are_preserved(string serviceName, bool sameHandler)
    {
        using var ctx = ValidationSyncContext.Install();

        using var client = new RemotingClient(new ClientConfig()
        {
            ConnectionTimeout = 0,
            InvocationTimeout = 0,
            SendTimeout = 0,
            Channel = ClientChannel,
            MessageEncryption = false,
            ServerPort = _serverFixture.Server.Config.NetworkPort,
        });

        client.Connect();

        var proxy = client.CreateProxy<ITestService>(serviceName);
        var fired = new AsyncCounter();

        // Two lambda expressions at different source positions compile to
        // two distinct methods, so their delegates are not equal. When
        // sameHandler is true, all three variables point to the same delegate.
        var handler1 = () => { fired++; };
        var handler2 = sameHandler ? handler1 : () => fired++;
        var handler3 = sameHandler ? handler1 : () => fired++;

        // Three subscriptions -> three invocations per fire.
        proxy.ServiceEvent += handler1;
        proxy.ServiceEvent += handler2;
        proxy.ServiceEvent += handler3;
        proxy.FireServiceEvent();
        await fired.WaitForValue(3).Timeout(1).ConfigureAwait(false);
        Assert.Equal(3, fired.Value);

        // Remove one subscription -> two invocations per fire.
        proxy.ServiceEvent -= handler1;
        proxy.FireServiceEvent();
        await fired.WaitForValue(5).Timeout(1).ConfigureAwait(false);
        Assert.Equal(5, fired.Value);

        // Remove one more -> one invocation per fire.
        proxy.ServiceEvent -= handler2;
        proxy.FireServiceEvent();
        await fired.WaitForValue(6).Timeout(1).ConfigureAwait(false);
        Assert.Equal(6, fired.Value);

        // Remove the last one -> no invocations.
        proxy.ServiceEvent -= handler3;
        proxy.FireServiceEvent();
        Assert.False(await fired.WaitForValue(7).Expire(0.5).ConfigureAwait(false));
        Assert.Equal(6, fired.Value);

        CheckServerErrorCount();
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task Shutting_down_one_proxy_doesnt_break_subscription_on_another()
    {
        using var ctx = ValidationSyncContext.Install();

        using var client = new RemotingClient(new ClientConfig()
        {
            ConnectionTimeout = 0,
            InvocationTimeout = 0,
            SendTimeout = 0,
            Channel = ClientChannel,
            MessageEncryption = false,
            ServerPort = _serverFixture.Server.Config.NetworkPort,
        });

        client.Connect();

        var proxy1 = client.CreateProxy<ITestService>("TestService_Singleton_Service");
        var proxy2 = client.CreateProxy<ITestService>("TestService_Singleton_Factory");

        var fired = new AsyncCounter();
        Action handler = () => { fired++; };

        // Same delegate subscribed to events of two different proxies.
        proxy1.ServiceEvent += handler;
        proxy2.ServiceEvent += handler;

        // Both subscriptions are live.
        proxy1.FireServiceEvent();
        await fired.WaitForValue(1).Timeout(1).ConfigureAwait(false);
        Assert.Equal(1, fired.Value);

        proxy2.FireServiceEvent();
        await fired.WaitForValue(2).Timeout(1).ConfigureAwait(false);
        Assert.Equal(2, fired.Value);

        // Shut down the first proxy. Only its own subscription must be
        // unregistered. The subscription made through the second proxy
        // must survive.
        client.ShutdownProxy(proxy1);

        proxy2.FireServiceEvent();
        await fired.WaitForValue(3).Timeout(1).ConfigureAwait(false);
        Assert.Equal(3, fired.Value);

        CheckServerErrorCount();
    }

    // Fun fact:
    // - ServerConfig.MessageEncryption setting doesn't affect anything!
    // - RemotingServer accepts both encrypted and unencrypted connections.

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task Disposed_client_subscription_doesnt_break_other_clients(bool encryption)
    {
        using var ctx = ValidationSyncContext.Install();

        try
        {
            RemotingClient CreateClient() => new(new()
            {
                Channel = ClientChannel,
                ServerPort = _serverFixture.Server.Config.NetworkPort,
                MessageEncryption = encryption,
                ConnectionTimeout = encryption ? 0 : 5,
                KeySize = 1024
            });

            using var client1 = CreateClient();
            using var client2 = CreateClient();

            client1.Connect();
            client2.Connect();

            var proxy1 = client1.CreateProxy<ITestService>();
            var fired1 = new TaskCompletionSource<bool>();
            proxy1.ServiceEvent += () => fired1.TrySetResult(true);

            var proxy2 = client2.CreateProxy<ITestService>();
            var fired2 = new TaskCompletionSource<bool>();
            proxy2.ServiceEvent += () => fired2.TrySetResult(true);

            // Early disposal: proxy1 subscription isn't canceled, but only client2
            // must receive the event.
            client1.Disconnect();

            proxy2.FireServiceEvent();
            Assert.True(await fired2.Task.ConfigureAwait(false));
            Assert.True(fired2.Task.IsCompleted);
            Assert.False(fired1.Task.IsCompleted);
        }
        finally
        {
            // Reset the error counter for other tests.
            _serverFixture.ServerErrorCount = 0;
        }
    }
}

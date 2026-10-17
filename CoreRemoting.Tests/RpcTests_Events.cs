using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.Tests.Tools;
using CoreRemoting.Threading;
using CoreRemoting.Toolbox;
using Xunit;

namespace CoreRemoting.Tests;

public partial class RpcTests
{
    private async Task<SubscriptionTracker> GetServerTrackerAsync(RemotingClient client)
    {
        var sessionId = client.SessionId ?? throw new InvalidOperationException("Client has no active session.");
        var sessionRepository = _serverFixture.Server.SessionRepository as SessionRepository;
        var session = await sessionRepository.GetSession(sessionId).ConfigureAwait(false);
        return session.SubscriptionTracker;
    }

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

        var serverTracker = await GetServerTrackerAsync(client);
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
        Assert.Equal(3, client.SubscriptionTracker.Count);
        Assert.Equal(client.SubscriptionTracker.Checksum, serverTracker.Checksum);

        proxy.FireServiceEvent();
        await fired.WaitForValue(3).Timeout(1).ConfigureAwait(false);
        Assert.Equal(3, fired.Value);

        // Remove one subscription -> two invocations per fire.
        proxy.ServiceEvent -= handler1;
        Assert.Equal(2, client.SubscriptionTracker.Count);
        Assert.Equal(client.SubscriptionTracker.Checksum, serverTracker.Checksum);

        proxy.FireServiceEvent();
        await fired.WaitForValue(5).Timeout(1).ConfigureAwait(false);
        Assert.Equal(5, fired.Value);

        // Remove one more -> one invocation per fire.
        proxy.ServiceEvent -= handler2;
        Assert.Equal(1, client.SubscriptionTracker.Count);
        Assert.Equal(client.SubscriptionTracker.Checksum, serverTracker.Checksum);

        proxy.FireServiceEvent();
        await fired.WaitForValue(6).Timeout(1).ConfigureAwait(false);
        Assert.Equal(6, fired.Value);

        // Remove the last one -> no invocations.
        proxy.ServiceEvent -= handler3;
        Assert.Equal(0, client.SubscriptionTracker.Count);
        Assert.Equal(client.SubscriptionTracker.Checksum, serverTracker.Checksum);

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

        var serverTracker = await GetServerTrackerAsync(client);
        var proxy1 = client.CreateProxy<ITestService>("TestService_Singleton_Service");
        var proxy2 = client.CreateProxy<ITestService>("TestService_Singleton_Factory");

        var fired = new AsyncCounter();
        Action handler = () => { fired++; };

        // Same delegate subscribed to events of two different proxies.
        proxy1.ServiceEvent += handler;
        proxy2.ServiceEvent += handler;

        Assert.Equal(2, client.SubscriptionTracker.Count);
        Assert.Equal(client.SubscriptionTracker.Checksum, serverTracker.Checksum);

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

        // ShutdownProxy does not notify the server (yet), so both trackers
        // remain unchanged and stay in sync. See the dedicated issue for
        // the server-side cleanup.
        Assert.Equal(client.SubscriptionTracker.Checksum, serverTracker.Checksum);

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

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task Subscription_trackers_stay_in_sync()
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

        var serverTracker = await GetServerTrackerAsync(client);

        Assert.Equal(client.SubscriptionTracker.Checksum, serverTracker.Checksum);

        var proxy1 = client.CreateProxy<ITestService>("TestService_Singleton_Service");
        var proxy2 = client.CreateProxy<ITestService>("TestService_Singleton_Factory");

        Action handler = () => { };

        proxy1.ServiceEvent += handler;
        Assert.Equal(client.SubscriptionTracker.Checksum, serverTracker.Checksum);

        // Same delegate on a different proxy counts as a second operation.
        proxy2.ServiceEvent += handler;
        Assert.Equal(client.SubscriptionTracker.Checksum, serverTracker.Checksum);
        Assert.Equal(2, client.SubscriptionTracker.Count);

        proxy1.ServiceEvent -= handler;
        Assert.Equal(client.SubscriptionTracker.Checksum, serverTracker.Checksum);

        proxy2.ServiceEvent -= handler;
        Assert.Equal(client.SubscriptionTracker.Checksum, serverTracker.Checksum);
        Assert.Equal(0, client.SubscriptionTracker.Count);

        CheckServerErrorCount();
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task Subscription_tracker_is_isolated_per_client()
    {
        using var ctx = ValidationSyncContext.Install();

        using var client1 = new RemotingClient(new ClientConfig()
        {
            ConnectionTimeout = 0,
            InvocationTimeout = 0,
            SendTimeout = 0,
            Channel = ClientChannel,
            MessageEncryption = false,
            ServerPort = _serverFixture.Server.Config.NetworkPort,
        });

        using var client2 = new RemotingClient(new ClientConfig()
        {
            ConnectionTimeout = 0,
            InvocationTimeout = 0,
            SendTimeout = 0,
            Channel = ClientChannel,
            MessageEncryption = false,
            ServerPort = _serverFixture.Server.Config.NetworkPort,
        });

        client1.Connect();
        client2.Connect();

        var serverTracker1 = await GetServerTrackerAsync(client1);
        var serverTracker2 = await GetServerTrackerAsync(client2);

        var proxy1 = client1.CreateProxy<ITestService>();
        var proxy2 = client2.CreateProxy<ITestService>();

        Action handler = () => { };

        proxy1.ServiceEvent += handler;
        proxy1.ServiceEvent += handler;
        proxy2.ServiceEvent += handler;

        Assert.Equal(2, client1.SubscriptionTracker.Count);
        Assert.Equal(1, client2.SubscriptionTracker.Count);

        Assert.Equal(client1.SubscriptionTracker.Checksum, serverTracker1.Checksum);
        Assert.Equal(client2.SubscriptionTracker.Checksum, serverTracker2.Checksum);
        Assert.NotEqual(serverTracker1.Checksum, serverTracker2.Checksum);

        CheckServerErrorCount();
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task Subscription_trackers_ignore_non_event_delegate_arguments()
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

        var serverTracker = await GetServerTrackerAsync(client);
        var proxy = client.CreateProxy<ITestService>();

        // Regular method that takes a delegate, not an event accessor.
        proxy.TestMethodWithDelegateArg(_ => { });

        Assert.Equal(0, client.SubscriptionTracker.Count);
        Assert.Equal(0, serverTracker.Count);
        Assert.Equal(client.SubscriptionTracker.Checksum, serverTracker.Checksum);

        CheckServerErrorCount();
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task Shutting_down_proxy_unsubscribes_from_server_side_event()
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

        var proxy = client.CreateProxy<ITestService>("TestService_Singleton_Service");
        var fired = new AsyncCounter();
        Action handler = () => { fired++; };

        proxy.ServiceEvent += handler;

        // The subscription is live.
        proxy.FireServiceEvent();
        await fired.WaitForValue(1).Timeout(1).ConfigureAwait(false);
        Assert.Equal(1, fired.Value);

        // Shutting down the proxy must unsubscribe from the server side as well.
        client.ShutdownProxy(proxy);

        // Fire the event again. The server must not invoke the dead subscription,
        // so the client-side handler must not be called.
        var serverProxy = client.CreateProxy<ITestService>("TestService_Singleton_Service");
        serverProxy.FireServiceEvent();

        // the handler is not invoked because the proxy was shut down.
        Assert.False(await fired.WaitForValue(2).Expire(0.5).ConfigureAwait(false));
        Assert.Equal(1, fired.Value);

        CheckServerErrorCount();
    }

    [Fact(Skip = "https://github.com/theRainbird/CoreRemoting/issues/245")]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task Shutting_down_proxy_unsubscribes_from_server_side_event_really()
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

        const string serviceName = "TestService_Singleton_Service";
        var proxy = client.CreateProxy<ITestService>(serviceName);
        var fired = new AsyncCounter();
        Action handler = () => { fired++; };

        proxy.ServiceEvent += handler;

        proxy.FireServiceEvent();
        await fired.WaitForValue(1).Timeout(1).ConfigureAwait(false);
        Assert.Equal(1, fired.Value);

        // Server side holds one subscription.
        var registration = _serverFixture.Server.ServiceRegistry.GetServiceRegistration(serviceName);
        var stub = registration.EventStub;

        var countBefore = EventStub.GetHandlerCount(stub[nameof(ITestService.ServiceEvent)]);
        Assert.Equal(1, countBefore);

        // Shutting down the proxy must unsubscribe from the server side as well.
        client.ShutdownProxy(proxy);

        // FAILS HERE: the server still holds the dead subscription.
        var countAfter = EventStub.GetHandlerCount(stub[nameof(ITestService.ServiceEvent)]);
        Assert.Equal(0, countAfter);

        CheckServerErrorCount();
    }
}

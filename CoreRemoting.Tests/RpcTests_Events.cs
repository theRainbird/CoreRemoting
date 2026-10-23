using System;
using System.Collections.Generic;
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

        var hub1 = _serverFixture.Server.ServiceRegistry
            .GetServiceRegistration("TestService_Singleton_Service").EventHub;
        var hub2 = _serverFixture.Server.ServiceRegistry
            .GetServiceRegistration("TestService_Singleton_Factory").EventHub;

        Assert.Equal(1, hub1.GetHandlerCount(nameof(ITestService.ServiceEvent)));
        Assert.Equal(1, hub2.GetHandlerCount(nameof(ITestService.ServiceEvent)));

        // Shut down the first proxy. Only its own subscription must be
        // unregistered. The subscription made through the second proxy
        // must survive.
        client.ShutdownProxy(proxy1);

        // SubscriptionTracker is bypassed by ShutdownProxy on both sides,
        // so both trackers remain stale and stay equal. This is intentional.
        Assert.Equal(client.SubscriptionTracker.Checksum, serverTracker.Checksum);

        // The actual server-side hub for proxy1 must lose the handler
        // (fire-and-forget, wait for it).
        await WaitUntilAsync(
            () => hub1.GetHandlerCount(nameof(ITestService.ServiceEvent)) == 0,
            timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // hub2 (proxy2) is untouched.
        Assert.Equal(1, hub2.GetHandlerCount(nameof(ITestService.ServiceEvent)));

        // And proxy2 still fires correctly.
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
    public async Task Subscription_trackers_can_detect_when_client_and_server_subscriptions_are_out_of_sync()
    {
        void BeforeCall(object sender, ServerRpcContext e)
        {
            if (e.MethodCallMessage.MethodName == "add_ServiceEvent")
                e.Cancel = true;
        }

        using var ctx = ValidationSyncContext.Install();
        _serverFixture.Server.BeforeCall += BeforeCall;

        try
        {
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
            var proxy = client.CreateProxy<ITestService>("TestService_Singleton_Service");

            Action handler = () => { };

            // Client tracker is updated before the RPC is sent.
            // Server tracker is updated only after BeforeCall, which is rejected.
            // The client sees an error, but its tracker has already counted the add.
            Assert.Throws<RemoteInvocationException>(() => { proxy.ServiceEvent += handler; });

            Assert.Equal(1, client.SubscriptionTracker.Count);
            Assert.Equal(0, serverTracker.Count);
            Assert.NotEqual(client.SubscriptionTracker.Checksum, serverTracker.Checksum);

            CheckServerErrorCount();
        }
        finally
        {
            _serverFixture.ServerErrorCount = 0;
            _serverFixture.Server.BeforeCall -= BeforeCall;
        }
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task Disconnecting_client_removes_all_subscriptions_on_server()
    {
        using var ctx = ValidationSyncContext.Install();
        using var client = CreateConnectedClient();

        const string serviceName = "TestService_Singleton_Service";
        var proxy = client.CreateProxy<ITestService>(serviceName);
        proxy.ServiceEvent += () => { };

        var registration = _serverFixture.Server.ServiceRegistry.GetServiceRegistration(serviceName);
        var hub = registration.EventHub;

        Assert.Equal(1, hub.GetHandlerCount(nameof(ITestService.ServiceEvent)));

        await client.DisconnectAsync().ConfigureAwait(false);

        await WaitUntilAsync(
            () => hub.GetHandlerCount(nameof(ITestService.ServiceEvent)) == 0,
            timeout: TimeSpan.FromSeconds(20)).ConfigureAwait(false);
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task Disconnect_does_not_block_on_in_flight_goodbye_counter()
    {
        const int largeDisposeTimeout = 5000;
        const int expectedDisposeWindow = 1;

        var config = _serverFixture.Server.Config;
        var oldTimeout = config.WaitTimeForCurrentlyProcessedMessagesOnDispose;
        config.WaitTimeForCurrentlyProcessedMessagesOnDispose = largeDisposeTimeout;

        try
        {
            using var ctx = ValidationSyncContext.Install();
            using var client = CreateConnectedClient();

            var sessionId = client.SessionId!.Value;
            var sessionRepo = (SessionRepository)_serverFixture.Server.SessionRepository;

            // the server must register the session before sending the handshake,
            // so a connected client always observes its session in the repository
            var session = await sessionRepo.GetSession(sessionId).ConfigureAwait(false);
            Assert.NotNull(session);

            var beforeDisposeFired = new TaskCompletionSource<bool>();
            session.BeforeDispose += () => beforeDisposeFired.TrySetResult(true);

            client.Disconnect();

            // if goodbye is still counted as in-flight when dispose starts,
            // dispose waits the whole WaitTimeForCurrentlyProcessedMessagesOnDispose
            // and BeforeDispose fires only after that timeout expires
            await beforeDisposeFired.Task.Timeout(expectedDisposeWindow).ConfigureAwait(false);
        }
        finally
        {
            config.WaitTimeForCurrentlyProcessedMessagesOnDispose = oldTimeout;
        }
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

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task Shutting_down_proxy_unsubscribes_from_server_side_event_sync()
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
        var hub = registration.EventHub;

        Assert.Equal(1, hub.GetHandlerCount(nameof(ITestService.ServiceEvent)));

        // ShutdownProxy fires the server-side removal asynchronously: wait for it.
        client.ShutdownProxy(proxy);

        await WaitUntilAsync(
            () => hub.GetHandlerCount(nameof(ITestService.ServiceEvent)) == 0,
            timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        CheckServerErrorCount();
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task Shutting_down_proxy_unsubscribes_from_server_side_event_async()
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
        var hub = registration.EventHub;

        Assert.Equal(1, hub.GetHandlerCount(nameof(ITestService.ServiceEvent)));

        // ShutdownProxyAsync awaits the server-side removal: deterministic,
        // no WaitUntilAsync needed.
        await client.ShutdownProxyAsync(proxy).Timeout(5).ConfigureAwait(false);

        Assert.Equal(0, hub.GetHandlerCount(nameof(ITestService.ServiceEvent)));
        Assert.Equal(0, client.PendingUnsubscribeCount);

        CheckServerErrorCount();
    }

    // --- ScheduleServerUnsubscribe ---

    [Fact]
    public async Task ScheduleServerUnsubscribe_with_empty_input_is_noop()
    {
        using var client = CreateConnectedClient();

        await client.ScheduleServerUnsubscribe([]).Timeout(1);
        await client.ScheduleServerUnsubscribe(null).Timeout(1);

        Assert.Equal(0, client.PendingUnsubscribeCount);
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task ScheduleServerUnsubscribe_removes_subscription_on_server()
    {
        using var ctx = ValidationSyncContext.Install();
        using var client = CreateConnectedClient();

        const string serviceName = "TestService_Singleton_Service";
        var proxy = client.CreateProxy<ITestService>(serviceName);
        Action handler = () => { };
        proxy.ServiceEvent += handler;

        var registration = _serverFixture.Server.ServiceRegistry
            .GetServiceRegistration(serviceName);
        var hub = registration.EventHub;

        Assert.Equal(1, hub.GetHandlerCount(nameof(ITestService.ServiceEvent)));

        // Unregister locally to capture keys WITHOUT notifying the server —
        // the server still holds the subscription until ScheduleServerUnsubscribe runs.
        // Use the interceptor (not the Castle proxy) — that is what the registry stores.
        var interceptor = RemotingClient.FindServiceProxy(proxy);
        Assert.NotNull(interceptor);

        var keys = client.ClientDelegateRegistry
            .UnregisterClientDelegatesOfServiceProxy(interceptor);
        Assert.Single(keys);

        await client.ScheduleServerUnsubscribe(keys).Timeout(5).ConfigureAwait(false);

        await WaitUntilAsync(
            () => hub.GetHandlerCount(nameof(ITestService.ServiceEvent)) == 0,
            timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        CheckServerErrorCount();
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task ScheduleServerUnsubscribe_is_noop_when_disconnected()
    {
        var client = CreateConnectedClient();

        try
        {
            client.Disconnect();

            var errors = new List<Exception>();
            client.ErrorHandler = ex => errors.Add(ex);

            await client.ScheduleServerUnsubscribe([Guid.NewGuid()]).Timeout(1).ConfigureAwait(false);

            Assert.Empty(errors);
            Assert.Equal(0, client.PendingUnsubscribeCount);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task ScheduleServerUnsubscribe_cleans_pending_map_after_completion()
    {
        using var client = CreateConnectedClient();

        // No real subscription needed: RemoveHandlers is idempotent on the server
        // (unknown keys return 0), and this test only verifies the client-side
        // pending-map cleanup.
        var key = Guid.NewGuid();

        await client.ScheduleServerUnsubscribe([key]).Timeout(5).ConfigureAwait(false);

        Assert.Equal(0, client.PendingUnsubscribeCount);
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task Invoke_for_unknown_handler_triggers_server_side_removal()
    {
        using var ctx = ValidationSyncContext.Install();
        using var client = CreateConnectedClient();

        const string serviceName = "TestService_Singleton_Service";
        var proxy = client.CreateProxy<ITestService>(serviceName);
        var fired = new AsyncCounter();
        Action handler = () => { fired++; };
        proxy.ServiceEvent += handler;

        var registration = _serverFixture.Server.ServiceRegistry
            .GetServiceRegistration(serviceName);
        var hub = registration.EventHub;

        Assert.Equal(1, hub.GetHandlerCount(nameof(ITestService.ServiceEvent)));

        // Drop the handler on the client only: the server still holds the
        // subscription and doesn't know it's dead yet.
        var interceptor = RemotingClient.FindServiceProxy(proxy);
        Assert.NotNull(interceptor);

        var keys = client.ClientDelegateRegistry
            .UnregisterClientDelegatesOfServiceProxy(interceptor);
        Assert.Single(keys);

        // Sanity check: the server-side hub is still intact at this point.
        Assert.Equal(1, hub.GetHandlerCount(nameof(ITestService.ServiceEvent)));

        // Fire the event: the server sends an "invoke" for the dead handler,
        // the client fails to resolve it locally, and schedules the nack.
        var serverProxy = client.CreateProxy<ITestService>(serviceName);
        serverProxy.FireServiceEvent();

        // The nack is fire-and-forget, so the server-side cleanup is
        // eventually consistent: wait for it.
        await WaitUntilAsync(
            () => hub.GetHandlerCount(nameof(ITestService.ServiceEvent)) == 0,
            timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // The dead handler was never invoked locally.
        Assert.Equal(0, fired.Value);

        CheckServerErrorCount();
    }

    // --- ShutdownProxy idempotency ---

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task ShutdownProxy_is_idempotent()
    {
        using var ctx = ValidationSyncContext.Install();
        using var client = CreateConnectedClient();

        const string serviceName = "TestService_Singleton_Service";
        var proxy = client.CreateProxy<ITestService>(serviceName);
        Action handler = () => { };
        proxy.ServiceEvent += handler;

        var registration = _serverFixture.Server.ServiceRegistry
            .GetServiceRegistration(serviceName);
        var hub = registration.EventHub;

        // First call unregisters and fires the RPC. Second call must be a
        // no-op: registry already empty, Shutdown already invoked.
        client.ShutdownProxy(proxy);
        client.ShutdownProxy(proxy);

        await WaitUntilAsync(
            () => hub.GetHandlerCount(nameof(ITestService.ServiceEvent)) == 0,
            timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        Assert.Equal(0, hub.GetHandlerCount(nameof(ITestService.ServiceEvent)));
        CheckServerErrorCount();
    }

    // --- Delegate argument disposal ---

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task ShutdownProxyAsync_marks_delegate_argument_as_disposed()
    {
        using var ctx = ValidationSyncContext.Install();
        using var client = CreateConnectedClient();

        _serverFixture.TestService.LastReceivedDelegate = null;

        var proxy = client.CreateProxy<ITestService>("TestService_Singleton_Service");
        proxy.TestMethodWithDelegateArg(_ => { });

        var stored = _serverFixture.TestService.LastReceivedDelegate;
        Assert.NotNull(stored);
        Assert.False(stored.IsDisposed());

        var sessionId = client.SessionId!.Value;
        var sessionRepo = (SessionRepository)_serverFixture.Server.SessionRepository;
        var session = await sessionRepo.GetSession(sessionId).ConfigureAwait(false);

        Assert.Equal(1, session._delegateProxyCache?.Count ?? 0);

        await client.ShutdownProxyAsync(proxy).ConfigureAwait(false);

        Assert.True(stored.IsDisposed());
        Assert.Equal(0, session._delegateProxyCache?.Count ?? 0);

        // invoking a disposed delegate is a silent no-op
        var ex = Record.Exception(() => stored("x"));
        Assert.Null(ex);

        CheckServerErrorCount();
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task ShutdownProxy_marks_delegate_argument_as_disposed()
    {
        using var ctx = ValidationSyncContext.Install();
        using var client = CreateConnectedClient();

        _serverFixture.TestService.LastReceivedDelegate = null;

        var proxy = client.CreateProxy<ITestService>("TestService_Singleton_Service");
        proxy.TestMethodWithDelegateArg(_ => { });

        var stored = _serverFixture.TestService.LastReceivedDelegate;
        Assert.NotNull(stored);
        Assert.False(stored.IsDisposed());

        client.ShutdownProxy(proxy);

        await WaitUntilAsync(
            () => stored.IsDisposed(),
            timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        CheckServerErrorCount();
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task Dispose_of_client_marks_delegate_argument_as_disposed()
    {
        using var ctx = ValidationSyncContext.Install();

        _serverFixture.TestService.LastReceivedDelegate = null;

        var client = CreateConnectedClient();
        var proxy = client.CreateProxy<ITestService>("TestService_Singleton_Service");
        proxy.TestMethodWithDelegateArg(_ => { });

        var stored = _serverFixture.TestService.LastReceivedDelegate;
        Assert.NotNull(stored);
        Assert.False(stored.IsDisposed());

        await client.DisposeAsync().ConfigureAwait(false);

        await WaitUntilAsync(
            () => stored.IsDisposed(),
            timeout: TimeSpan.FromSeconds(20)).ConfigureAwait(false);
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task Event_unsubscribe_does_not_dispose_delegate_argument()
    {
        using var ctx = ValidationSyncContext.Install();
        using var client = CreateClient();
        await client.ConnectAsync().ConfigureAwait(false);

        _serverFixture.TestService.LastReceivedDelegate = null;

        var proxy = client.CreateProxy<ITestService>("TestService_Singleton_Service");

        // subscribe + unsubscribe an event — must not touch delegate argument cache
        Action handler = () => { };
        proxy.ServiceEvent += handler;
        proxy.ServiceEvent -= handler;

        // now pass a plain delegate argument, then dispose the proxy
        proxy.TestMethodWithDelegateArg(_ => { });
        var stored = _serverFixture.TestService.LastReceivedDelegate;
        Assert.NotNull(stored);

        // -= above must not have invalidated the plain delegate argument
        Assert.False(stored.IsDisposed());

        CheckServerErrorCount();
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task ShutdownProxy_does_not_affect_other_clients_delegate_arguments()
    {
        using var ctx = ValidationSyncContext.Install();

        _serverFixture.TestService.LastReceivedDelegate = null;

        using var client1 = CreateConnectedClient();
        using var client2 = CreateConnectedClient();

        var proxy1 = client1.CreateProxy<ITestService>("TestService_Singleton_Service");
        proxy1.TestMethodWithDelegateArg(_ => { });
        var stored1 = _serverFixture.TestService.LastReceivedDelegate;
        Assert.NotNull(stored1);

        var proxy2 = client2.CreateProxy<ITestService>("TestService_Singleton_Service");
        proxy2.TestMethodWithDelegateArg(_ => { });
        var stored2 = _serverFixture.TestService.LastReceivedDelegate;
        Assert.NotNull(stored2);
        Assert.NotSame(stored1, stored2);

        await client1.ShutdownProxyAsync(proxy1).ConfigureAwait(false);

        Assert.True(stored1.IsDisposed());
        Assert.False(stored2.IsDisposed());

        CheckServerErrorCount();
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task Zyan_pattern_end_to_end()
    {
        using var ctx = ValidationSyncContext.Install();

        _serverFixture.TestService.LastReceivedDelegate = null;

        using var client1 = CreateConnectedClient();
        var client2 = CreateConnectedClient();

        var proxy1 = client1.CreateProxy<ITestService>("TestService_Singleton_Service");
        proxy1.TestMethodWithDelegateArg(_ => { });

        var stored1 = _serverFixture.TestService.LastReceivedDelegate;
        Assert.NotNull(stored1);

        var proxy2 = client2.CreateProxy<ITestService>("TestService_Singleton_Service");
        proxy2.TestMethodWithDelegateArg(_ => { });

        var stored2 = _serverFixture.TestService.LastReceivedDelegate;
        Assert.NotNull(stored2);

        try
        {
            await client1.DisposeAsync().ConfigureAwait(false);

            await WaitUntilAsync(
                () => stored1.IsDisposed(),
                timeout: TimeSpan.FromSeconds(20)).ConfigureAwait(false);

            // user code pattern: invoke + cleanup
            var callbacks = new List<Action<string>> { stored1, stored2 };

            foreach (var cb in callbacks)
                cb("hello"); // dead -> silent no-op

            callbacks.RemoveAll(cb => cb.IsDisposed());

            Assert.Single(callbacks);
            Assert.Same(stored2, callbacks[0]);
            Assert.False(stored2.IsDisposed());
        }
        finally
        {
            client2.Dispose();
        }

        CheckServerErrorCount();
    }
}

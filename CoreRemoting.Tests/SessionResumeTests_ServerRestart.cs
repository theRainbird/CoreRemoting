using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Tcp;
using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests.Sessions;

[Collection("CoreRemoting")]
public class SessionResumeTests_ServerRestart : SessionResumeTests_Base
{
    protected override IServerChannel ServerChannel => new TcpServerChannel(); // new WebsocketServerChannel(); // new TcpServerChannel();
    protected override IClientChannel ClientChannel => new TcpClientChannel(); // new WebsocketClientChannel(); // new TcpClientChannel();
    protected override bool MessageEncryption => false;
    protected override bool AuthenticationRequiredForResumeTests => false;
    protected override bool AuthenticationRequiredForNegativeTests => false;

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Failed_invoke_due_to_disconnect_releases_pending_message_counter()
    {
        using var ctx = ValidationSyncContext.Install();
        int networkPort = Interlocked.Increment(ref _nextPort);
        var server = StartServer(networkPort, AuthenticationRequiredForNegativeTests);
        try
        {
            using var client = CreateClient(networkPort, AuthenticationRequiredForNegativeTests);
            await client.ConnectAsync().ConfigureAwait(false);

            var proxy = client.CreateProxy<ITestService>();
            Assert.Equal("test", await proxy.EchoAsync("test"));

            var baseline = client.PendingMessageCount;

            // simulate a concurrent disconnect between ensure-connected and the active-calls lock
            await client.SimulateDisconnectRace();

            var ex = await Assert.ThrowsAsync<RemoteInvocationException>(() => proxy.EchoAsync("test"));
            Assert.Equal("Server disconnected", ex.Message);

            // the counter must be released on the failure path, not leaked
            Assert.Equal(baseline, client.PendingMessageCount);
        }
        finally
        {
            await Task.Delay(500).ConfigureAwait(false);
            CheckServerErrorCount();
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Disconnected_client_should_resume_session_after_server_restart()
    {
        using var ctx = ValidationSyncContext.Install();

        int networkPort = Interlocked.Increment(ref _nextPort);

        var serverA = StartServer(networkPort, AuthenticationRequiredForNegativeTests);

        try
        {
            using var client = CreateClient(networkPort, AuthenticationRequiredForNegativeTests);

            await client.ConnectAsync().ConfigureAwait(false);

            Guid? originalSessionId = client.SessionId;
            Assert.NotNull(originalSessionId);

            var proxy = client.CreateProxy<ITestService>();
            Assert.Equal("test", proxy.TestMethod("test"));

            await client.HardKill().ConfigureAwait(false);

            var repoA = (SessionRepository)serverA.SessionRepository;
            Assert.Single(repoA.Sessions);
            Assert.True(repoA.Sessions.Single().IsParked);

            await serverA.HardKill().ConfigureAwait(false);

            var repoB = new SessionRepository(repoA,
                keySize: KeySize,
                inactiveSessionSweepInterval: 0,
                maximumSessionInactivityTime: 30);

            Assert.Single(repoB.Sessions);
            Assert.Empty(repoA.Sessions);

            await serverA.DisposeAsync().ConfigureAwait(false);

            var serverB = StartServer(networkPort,
                AuthenticationRequiredForNegativeTests,
                repository: repoB);

            try
            {
                await client.ConnectAsync().ConfigureAwait(false);

                Assert.Equal(originalSessionId, client.SessionId);
                Assert.Single(repoB.Sessions);

                var resumed = repoB.Sessions.Single();
                Assert.False(resumed.IsParked);
                Assert.Same(serverB, resumed.Server);

                Assert.Equal("test", proxy.TestMethod("test"));
            }
            finally
            {
                await Task.Delay(500).ConfigureAwait(false);
                CheckServerErrorCount();
                await serverB.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await Task.Delay(500).ConfigureAwait(false);
        }
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Parallel_connect_calls_establish_a_single_session()
    {
        using var ctx = ValidationSyncContext.Install();
        int networkPort = Interlocked.Increment(ref _nextPort);
        var server = StartServer(networkPort, AuthenticationRequiredForNegativeTests);
        try
        {
            using var client = CreateClient(networkPort, AuthenticationRequiredForNegativeTests);

            var tasks = Enumerable.Range(0, 5)
                .Select(_ => client.ConnectAsync())
                .ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            Assert.True(client.IsConnected);
            Assert.True(client.HasSession);

            var repo = (SessionRepository)server.SessionRepository;
            Assert.Single(repo.Sessions);
        }
        finally
        {
            await Task.Delay(500).ConfigureAwait(false);
            CheckServerErrorCount();
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Parallel_rpc_calls_after_client_restart_share_a_single_resumed_session()
    {
        using var ctx = ValidationSyncContext.Install();
        int networkPort = Interlocked.Increment(ref _nextPort);

        var serverA = StartServer(networkPort, AuthenticationRequiredForNegativeTests);
        try
        {
            using var client = CreateClient(networkPort, AuthenticationRequiredForNegativeTests);
            await client.ConnectAsync().ConfigureAwait(false);

            Guid? originalSessionId = client.SessionId;
            var proxy = client.CreateProxy<ITestService>();
            Assert.Equal("test", proxy.TestMethod("test"));

            await client.HardKill().ConfigureAwait(false);
            await serverA.HardKill().ConfigureAwait(false);

            var repoA = (SessionRepository)serverA.SessionRepository;
            var repoB = new SessionRepository(repoA, KeySize, 0, 30);
            await serverA.DisposeAsync().ConfigureAwait(false);

            var serverB = StartServer(networkPort, AuthenticationRequiredForNegativeTests, repository: repoB);
            try
            {
                // three rpcs race to reconnect; only one resume should occur
                var results = await Task.WhenAll(
                    Task.Run(() => proxy.TestMethod("a")),
                    Task.Run(() => proxy.TestMethod("b")),
                    Task.Run(() => proxy.TestMethod("c"))
                ).ConfigureAwait(false);

                Assert.Equal(["a", "b", "c"], results);
                Assert.Equal(originalSessionId, client.SessionId);

                var resumed = repoB.Sessions.Single();
                Assert.False(resumed.IsParked);
                Assert.Same(serverB, resumed.Server);
            }
            finally
            {
                await Task.Delay(500).ConfigureAwait(false);
                CheckServerErrorCount();
                await serverB.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await Task.Delay(500).ConfigureAwait(false);
        }
    }

    [Fact(Skip = "Hangs occasionally")]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Server_crash_is_not_detected_by_client_until_next_remote_call()
    {
        using var ctx = ValidationSyncContext.Install();
        int networkPort = Interlocked.Increment(ref _nextPort);

        using var serverA = StartServer(networkPort, AuthenticationRequiredForNegativeTests);
        using var client = CreateClient(networkPort, AuthenticationRequiredForNegativeTests);
        await client.ConnectAsync().ConfigureAwait(false);

        Guid? originalSessionId = client.SessionId;
        var proxy = client.CreateProxy<ITestService>();
        Assert.Equal("test", proxy.TestMethod("test"));

        await serverA.HardKill().ConfigureAwait(false);

        var repoA = (SessionRepository)serverA.SessionRepository;
        var repoB = new SessionRepository(repoA, KeySize, 0, 30);
        await serverA.DisposeAsync().ConfigureAwait(false);

        // at this point, client is unaware of the server shutdown
        Assert.True(client.IsConnected);

        // when a client tries RPC, it fails because of the broken transport
        var ex = await Assert.ThrowsAsync<RemoteInvocationException>(() => proxy.EchoAsync("should fail")).ConfigureAwait(false);

        Assert.False(client.IsConnected);
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Parallel_rpc_calls_after_server_restart_share_a_single_resumed_session()
    {
        using var ctx = ValidationSyncContext.Install();
        int networkPort = Interlocked.Increment(ref _nextPort);

        var serverA = StartServer(networkPort, AuthenticationRequiredForNegativeTests);
        try
        {
            using var client = CreateClient(networkPort, AuthenticationRequiredForNegativeTests);
            await client.ConnectAsync().ConfigureAwait(false);

            Guid? originalSessionId = client.SessionId;
            var proxy = client.CreateProxy<ITestService>();
            Assert.Equal("test", proxy.TestMethod("test"));

            // server A crashes; parked sessions are handed over to a new repository
            await serverA.HardKill().ConfigureAwait(false);

            var repoA = (SessionRepository)serverA.SessionRepository;
            var repoB = new SessionRepository(repoA, KeySize, 0, 30);
            await serverA.DisposeAsync().ConfigureAwait(false);

            // the client notices the disconnect (transport closed by the dead server)
            await WaitUntilAsync(() => !client.IsConnected, TimeSpan.FromSeconds(5));

            var serverB = StartServer(networkPort, AuthenticationRequiredForNegativeTests, repository: repoB);

            try
            {
                // three rpcs race to lazily reconnect; only one resume should occur
                var results = await Task.WhenAll(
                    Task.Run(() => proxy.TestMethod("a")),
                    Task.Run(() => proxy.TestMethod("b")),
                    Task.Run(() => proxy.TestMethod("c"))
                ).ConfigureAwait(false);

                Assert.Equal(["a", "b", "c"], results);
                Assert.Equal(originalSessionId, client.SessionId);

                var resumed = repoB.Sessions.Single();
                Assert.False(resumed.IsParked);
                Assert.Same(serverB, resumed.Server);
            }
            finally
            {
                await Task.Delay(500).ConfigureAwait(false);
                CheckServerErrorCount();
                await serverB.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await Task.Delay(500).ConfigureAwait(false);
        }
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Client_should_survive_server_restart()
    {
        using var ctx = ValidationSyncContext.Install();

        int networkPort = Interlocked.Increment(ref _nextPort);

        var serverA = StartServer(networkPort, AuthenticationRequiredForNegativeTests);

        try
        {
            using var client = CreateClient(networkPort, AuthenticationRequiredForNegativeTests);

            // enable the heartbeat so the client proactively detects the dead server
            client.Config.KeepSessionAliveInterval = 1;

            await client.ConnectAsync().ConfigureAwait(false);

            Guid? originalSessionId = client.SessionId;
            Assert.NotNull(originalSessionId);

            var proxy = client.CreateProxy<ITestService>();
            Assert.Equal("test", proxy.TestMethod("test"));

            // server A crashes; sessions are parked, repo and DI stay alive
            await serverA.HardKill().ConfigureAwait(false);

            // client must notice the disconnect on its own
            await WaitUntilAsync(() => !client.IsConnected, TimeSpan.FromSeconds(5));
            Assert.False(client.IsConnected);

            // transfer parked sessions to a new repository
            var repoA = (SessionRepository)serverA.SessionRepository;
            Assert.Single(repoA.Sessions);
            Assert.True(repoA.Sessions.Single().IsParked);

            var repoB = new SessionRepository(repoA, KeySize, 0, 30);
            Assert.Empty(repoA.Sessions);
            Assert.Single(repoB.Sessions);

            await serverA.DisposeAsync().ConfigureAwait(false);

            // server B starts on the same port with the transferred repository
            var serverB = StartServer(networkPort,
                AuthenticationRequiredForNegativeTests,
                repository: repoB);

            try
            {
                // an rpc call triggers lazy reconnect against the restarted server
                Assert.Equal("test", proxy.TestMethod("test"));

                // the same session was resumed, not re-created
                Assert.Equal(originalSessionId, client.SessionId);
                Assert.True(client.IsConnected);

                var resumed = repoB.Sessions.Single();
                Assert.False(resumed.IsParked);
                Assert.Same(serverB, resumed.Server);

                // subsequent calls keep using the resumed connection
                Assert.Equal("test", proxy.TestMethod("test"));
            }
            finally
            {
                await Task.Delay(500).ConfigureAwait(false);
                CheckServerErrorCount();
                await serverB.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await Task.Delay(500).ConfigureAwait(false);
        }
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Event_subscriptions_survive_server_restart()
    {
        using var ctx = ValidationSyncContext.Install();
        int networkPort = Interlocked.Increment(ref _nextPort);

        var serverA = StartServer(networkPort, AuthenticationRequiredForNegativeTests);
        try
        {
            using var client = CreateClient(networkPort, AuthenticationRequiredForNegativeTests);
            await client.ConnectAsync().ConfigureAwait(false);

            var proxy = client.CreateProxy<ITestService>();

            int clientEventCount = 0;
            proxy.ServiceEvent += () => Interlocked.Increment(ref clientEventCount);

            // the subscription works against the original server
            proxy.FireServiceEvent();
            await WaitUntilAsync(() => Volatile.Read(ref clientEventCount) == 1, TimeSpan.FromSeconds(5));

            // server A crashes; parked sessions are handed over to a new repository
            await serverA.HardKill().ConfigureAwait(false);

            var repoA = (SessionRepository)serverA.SessionRepository;
            var repoB = new SessionRepository(repoA, KeySize, 0, 30);
            await serverA.DisposeAsync().ConfigureAwait(false);

            await WaitUntilAsync(() => !client.IsConnected, TimeSpan.FromSeconds(5));

            var serverB = StartServer(networkPort, AuthenticationRequiredForNegativeTests, repository: repoB);
            try
            {
                // a lazy rpc triggers reconnect + session resume + subscription replay
                Assert.Equal("test", proxy.TestMethod("test"));

                // the client's subscription state matches the resumed session on the new server
                await WaitUntilAsync(
                    () => repoB.Sessions.Single().SubscriptionTracker.Checksum ==
                         client.SubscriptionTracker.Checksum,
                    TimeSpan.FromSeconds(5));

                // events raised by the NEW server reach the replayed client handler
                proxy.FireServiceEvent();
                await WaitUntilAsync(() => Volatile.Read(ref clientEventCount) >= 2, TimeSpan.FromSeconds(5));

                Assert.Equal(2, Volatile.Read(ref clientEventCount));
            }
            finally
            {
                await Task.Delay(500).ConfigureAwait(false);
                CheckServerErrorCount();
                await serverB.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await Task.Delay(500).ConfigureAwait(false);
        }
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Manual_disconnect_does_not_trigger_auto_reconnect()
    {
        using var ctx = ValidationSyncContext.Install();
        int networkPort = Interlocked.Increment(ref _nextPort);

        var server = StartServer(networkPort, AuthenticationRequiredForNegativeTests);
        try
        {
            using var client = CreateClient(networkPort, AuthenticationRequiredForNegativeTests);
            client.Config.AutoReconnect = true;
            client.Config.ReconnectMinDelay = 1;
            client.Config.ReconnectMaxDelay = 2;

            await client.ConnectAsync().ConfigureAwait(false);
            Assert.True(client.IsConnected);

            int reconnectedCount = 0;
            client.Reconnected += (s, e) => Interlocked.Increment(ref reconnectedCount);

            // A manual disconnect must suppress the auto-reconnect loop.
            await client.DisconnectAsync().ConfigureAwait(false);
            Assert.False(client.IsConnected);

            // Wait well beyond the reconnect backoff window. With a broken suppression
            // (e.g. a readonly AtomicBool whose Set() is discarded) the client would
            // reconnect to the still-running server here.
            await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(false);

            Assert.False(client.IsConnected);
            Assert.Equal(0, Volatile.Read(ref reconnectedCount));
        }
        finally
        {
            await Task.Delay(500).ConfigureAwait(false);
            CheckServerErrorCount();
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Server_with_fallback_enabled_lets_client_continue_with_new_session_when_resume_refused()
    {
        using var ctx = ValidationSyncContext.Install();
        int networkPort = Interlocked.Increment(ref _nextPort);

        var server = StartServer(networkPort, AuthenticationRequiredForNegativeTests);
        try
        {
            // The server opts into letting clients fall back to a new session when a
            // resume is refused (this is also the default; strict mode requires
            // AllowSessionResumeFallback = false).
            server.Config.AllowSessionResumeFallback = true;

            var bogusSessionId = Guid.NewGuid();
            using var client = CreateClient(networkPort, AuthenticationRequiredForNegativeTests,
                resumableSessionId: bogusSessionId);

            // The requested session does not exist on the server, so the resume is
            // refused. Because the server allows the fallback, the connect must NOT throw.
            await client.ConnectAsync().ConfigureAwait(false);

            Assert.NotNull(client.SessionId);
            Assert.NotEqual(bogusSessionId, client.SessionId.Value);
        }
        finally
        {
            await Task.Delay(500).ConfigureAwait(false);
            CheckServerErrorCount();
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Default_config_recovers_after_server_restart_without_session_handover()
    {
        using var ctx = ValidationSyncContext.Install();
        int networkPort = Interlocked.Increment(ref _nextPort);

        var serverA = StartServer(networkPort, AuthenticationRequiredForNegativeTests);
        try
        {
            // no flags set anywhere: AutoReconnect and AllowSessionResumeFallback
            // are both enabled by default
            using var client = CreateClient(networkPort, AuthenticationRequiredForNegativeTests);
            await client.ConnectAsync().ConfigureAwait(false);

            var originalSessionId = client.SessionId;
            var proxy = client.CreateProxy<ITestService>();

            int clientEventCount = 0;
            proxy.ServiceEvent += () => Interlocked.Increment(ref clientEventCount);

            proxy.FireServiceEvent();
            await WaitUntilAsync(() => Volatile.Read(ref clientEventCount) == 1, TimeSpan.FromSeconds(5));

            // Capture the end of the reconnect pipeline. The Reconnected event fires
            // only once the handshake, the authentication and the subscription replay
            // are all complete. IsConnected must NOT be used for that: it is plain
            // transport state and turns true the moment the socket opens, while the
            // new session does not exist yet and the replayed subscriptions are not
            // registered on the server — an event fired in that window is lost.
            ReconnectedEventArgs reconnectedArgs = null;
            client.Reconnected += (_, e) => Interlocked.CompareExchange(ref reconnectedArgs, e, null);

            // server A crashes and its parked sessions are NOT handed over:
            // the remembered session is gone for good
            await serverA.HardKill().ConfigureAwait(false);
            await serverA.DisposeAsync().ConfigureAwait(false);

            await WaitUntilAsync(() => !client.IsConnected, TimeSpan.FromSeconds(5));

            var serverB = StartServer(networkPort, AuthenticationRequiredForNegativeTests);
            try
            {
                // the background auto-reconnect requests the resume of the lost
                // session; the server cannot honor it and — because the fallback is
                // enabled by default — the client silently continues with a NEW
                // session instead of throwing
                await WaitUntilAsync(() => Volatile.Read(ref reconnectedArgs) != null, TimeSpan.FromSeconds(20));

                var reconnect = reconnectedArgs;

                // the old session was gone, so nothing could be resumed
                Assert.False(reconnect.SessionResumed);

                // the client moved to a brand-new session instead of throwing
                Assert.NotEqual(originalSessionId, reconnect.SessionId);
                Assert.Equal(reconnect.SessionId, client.SessionId);

                // the event subscriptions were replayed and verified against the
                // new server before the reconnect was reported as finished
                Assert.True(reconnect.SubscriptionsRestored);

                // the replayed subscription receives events raised by the new server
                proxy.FireServiceEvent();
                await WaitUntilAsync(() => Volatile.Read(ref clientEventCount) >= 2, TimeSpan.FromSeconds(5));
            }
            finally
            {
                await Task.Delay(500).ConfigureAwait(false);
                CheckServerErrorCount();
                await serverB.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await Task.Delay(500).ConfigureAwait(false);
        }
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Late_disconnect_handler_from_old_connection_does_not_break_new_connection()
    {
        using var ctx = ValidationSyncContext.Install();
        int networkPort = Interlocked.Increment(ref _nextPort);
        var server = StartServer(networkPort, AuthenticationRequiredForNegativeTests);

        try
        {
            using var client = CreateClient(networkPort, AuthenticationRequiredForNegativeTests);
            await client.ConnectAsync().ConfigureAwait(false);
            var proxy = client.CreateProxy<ITestService>();

            // 1. Capture the generation of the initial connection
            var oldGeneration = client.ConnectionGenerationForTest;

            // 2. Reconnect: this bumps the generation and creates a new _activeCalls dictionary
            await client.DisconnectAsync().ConfigureAwait(false);
            await client.ConnectAsync().ConfigureAwait(false);

            // 3. Simulate a late handler from the OLD connection that just woke up.
            // Without the generation check, this would null out the new _activeCalls.
            await client.InvokeOnDisconnectedCoreAsync(oldGeneration).ConfigureAwait(false);

            // 4. The new connection must be completely unaffected,
            // keep-alive still running, RPC works.
            Assert.True(client.IsConnected);
            Assert.Equal("test", await proxy.EchoAsync("test").ConfigureAwait(false));
            Assert.Equal("test", await proxy.EchoAsync("test").ConfigureAwait(false));
        }
        finally
        {
            await Task.Delay(500).ConfigureAwait(false);
            CheckServerErrorCount();
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }
}

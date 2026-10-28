using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Tcp;
using CoreRemoting.Channels.Websocket;
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

    [Fact(Skip = "Hangs occasionally")]
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
            client.SimulateDisconnectRace();

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

    [Fact(Skip = "Hangs occasionally")]
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

    [Fact(Skip = "Hangs occasionally")]
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

    [Fact(Skip = "Not implemented yet")]
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

            //await client.HardKill().ConfigureAwait(false);
            await serverA.HardKill().ConfigureAwait(false);

            var repoA = (SessionRepository)serverA.SessionRepository;
            var repoB = new SessionRepository(repoA, KeySize, 0, 30);
            await serverA.DisposeAsync().ConfigureAwait(false);

            // at this point, client is unaware of the server shutdown
            Assert.True(client.IsConnected);

            // when a client tries RPC, it fails because of the broken transport
            var ex = await Assert.ThrowsAsync<NetworkException>(() => proxy.EchoAsync("should fail"));
            Assert.False(client.IsConnected);

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

    [Fact(Skip = "Not implemented")]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Client_should_survive_server_restart()
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
}

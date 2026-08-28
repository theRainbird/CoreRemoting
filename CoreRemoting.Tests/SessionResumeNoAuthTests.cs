using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Websocket;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests;

[Collection("CoreRemoting")]
public class SessionResumeNoAuthTests
{
    // custom port range for these tests (9300+ is used by SessionResumeTests)
    private static int _nextPort = 9400;

    [Fact]
    public async Task Client_should_resume_parked_session_after_abrupt_disconnect()
    {
        var serverErrorCount = 0;
        Exception lastServerError = null;
        int networkPort = Interlocked.Increment(ref _nextPort);

        var server = StartServer(
            networkPort: networkPort,
            onServerError: (s, ex) =>
            {
                Interlocked.Increment(ref serverErrorCount);
                lastServerError = ex;
            });

        try
        {
            using var client = CreateClient(networkPort);
            await client.ConnectAsync();

            Assert.True(client.HasSession);

            Guid? originalSessionId = client.SessionId;
            Assert.NotNull(originalSessionId);

            var proxy = client.CreateProxy<ITestService>();
            Assert.Equal("test", proxy.TestMethod("test"));

            // abruptly kill the transport (no goodbye message, session must be parked on server)
            await HardKill(client);

            // parked sessions stay in the repository until swept by inactivity
            Assert.Single(server.SessionRepository.Sessions);
            Assert.True(
                server.SessionRepository.Sessions.Single().IsParked,
                "Session should be parked after an abrupt disconnect");

            // reconnect the same client instance (same RSA key pair, session ID still known)
            await client.ConnectAsync();

            // server resumed the existing session instead of creating a new one
            Assert.Equal(originalSessionId, client.SessionId);
            Assert.Single(server.SessionRepository.Sessions);

            var resumed = server.SessionRepository.Sessions.Single();

            Assert.False(resumed.IsParked);
            Assert.Equal(originalSessionId.Value, resumed.SessionId);

            // RPC works again with the same session
            Assert.Equal("test", proxy.TestMethod("test"));
        }
        finally
        {
            await Task.Delay(500);

            if (lastServerError != null)
                throw new Exception($"Unexpected server error: {lastServerError}");

            Assert.Equal(0, serverErrorCount);

            server.Stop();
        }
    }

    [Fact]
    public async Task Client_with_persisted_key_material_should_resume_session_after_reinstantiation()
    {
        var serverErrorCount = 0;
        Exception lastServerError = null;
        int networkPort = Interlocked.Increment(ref _nextPort);

        var server = StartServer(
            networkPort: networkPort,
            onServerError: (s, ex) =>
            {
                Interlocked.Increment(ref serverErrorCount);
                lastServerError = ex;
            });

        try
        {
            using (var firstClient = CreateClient(networkPort))
            {
                await firstClient.ConnectAsync();

                Guid? sessionId = firstClient.SessionId;
                byte[] privateKeyBlob = firstClient.PrivateKey;

                Assert.NotNull(sessionId);
                Assert.NotNull(privateKeyBlob);

                // abruptly disconnect so the session is parked and keeps its key material
                await HardKill(firstClient);

                // simulate process restart: the app persisted session ID + client RSA private key,
                // a new client instance must resume the same session with the same identity
                using var secondClient = CreateClient(
                    networkPort,
                    rsaPrivateKeyBlob: privateKeyBlob,
                    resumableSessionId: sessionId);

                await secondClient.ConnectAsync();

                Assert.Equal(sessionId, secondClient.SessionId);
                Assert.Single(server.SessionRepository.Sessions);

                var resumed = server.SessionRepository.Sessions.Single();

                Assert.False(resumed.IsParked);
                Assert.Equal(sessionId.Value, resumed.SessionId);

                // RPC works again
                var proxy = secondClient.CreateProxy<ITestService>();
                Assert.Equal("test", proxy.TestMethod("test"));
            }
        }
        finally
        {
            await Task.Delay(500);

            if (lastServerError != null)
                throw new Exception($"Unexpected server error: {lastServerError}");

            Assert.Equal(0, serverErrorCount);

            server.Stop();
        }
    }

    [Fact]
    public async Task Client_with_mismatching_key_material_should_not_resume_parked_session()
    {
        var serverErrorCount = 0;
        Exception lastServerError = null;
        int networkPort = Interlocked.Increment(ref _nextPort);

        var server = StartServer(
            networkPort: networkPort,
            onServerError: (s, ex) =>
            {
                Interlocked.Increment(ref serverErrorCount);
                lastServerError = ex;
            });

        try
        {
            Guid sessionId;

            using (var firstClient = CreateClient(networkPort))
            {
                await firstClient.ConnectAsync();
                sessionId = client_GetSessionId(firstClient);
                await HardKill(firstClient);
            }

            // wrong RSA key + stale session ID: the server has to reject the resume attempt
            using var secondClient = CreateClient(
                networkPort,
                resumableSessionId: sessionId);

            var exception =
                await Assert.ThrowsAsync<RemotingException>(() => secondClient.ConnectAsync());

            Assert.Contains("refused to resume", exception.Message);

            // the rejected resume attempt led to a NEW session; the parked one is untouched
            Assert.Equal(2, server.SessionRepository.Sessions.Count());
            Assert.Contains(server.SessionRepository.Sessions, s => s.IsParked);
        }
        finally
        {
            await Task.Delay(500);

            if (lastServerError != null)
                throw new Exception($"Unexpected server error: {lastServerError}");

            Assert.Equal(0, serverErrorCount);

            server.Stop();
        }
    }

    [Fact]
    public async Task Resumable_session_id_should_fail_when_session_was_disposed_gently()
    {
        var serverErrorCount = 0;
        Exception lastServerError = null;
        int networkPort = Interlocked.Increment(ref _nextPort);

        var server = StartServer(
            networkPort: networkPort,
            onServerError: (s, ex) =>
            {
                Interlocked.Increment(ref serverErrorCount);
                lastServerError = ex;
            });

        try
        {
            Guid sessionId;

            using (var firstClient = CreateClient(networkPort))
            {
                await firstClient.ConnectAsync();
                sessionId = client_GetSessionId(firstClient);

                // graceful disconnect sends a goodbye message and disposes the session (no parking)
                await firstClient.DisposeAsync();
                await Task.Delay(500);

                Assert.Empty(server.SessionRepository.Sessions);
            }

            using var secondClient = CreateClient(
                networkPort,
                resumableSessionId: sessionId);

            // session no longer exists, so the server created a new one and the strict check fails
            var exception =
                await Assert.ThrowsAsync<RemotingException>(() => secondClient.ConnectAsync());

            Assert.Contains("refused to resume", exception.Message);
        }
        finally
        {
            await Task.Delay(500);

            if (lastServerError != null)
                throw new Exception($"Unexpected server error: {lastServerError}");

            Assert.Equal(0, serverErrorCount);

            server.Stop();
        }
    }

    private static Guid client_GetSessionId(RemotingClient client)
    {
        var sessionId = client.SessionId;
        Assert.NotNull(sessionId);
        return sessionId.Value;
    }

    /// <summary>
    /// Kills the client transport without sending a goodbye message (simulates network failure).
    /// </summary>
    private static async Task HardKill(RemotingClient client)
    {
        var channel =
            (IClientChannel)typeof(RemotingClient).GetField(
                "_channel", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(client);

        await channel.DisconnectAsync();

        // wait for the server-side disconnect handling (parking) to complete
        await Task.Delay(500);
    }

    private static RemotingClient CreateClient(
        int serverPort,
        byte[] rsaPrivateKeyBlob = null,
        Guid? resumableSessionId = null)
    {
        var config = new ClientConfig()
        {
            ConnectionTimeout = 0,
            MessageEncryption = false,
            //Channel = new WebsocketClientChannel(), // FIXME
            ServerHostName = "localhost",
            ServerPort = serverPort,
            KeepSessionAliveInterval = 0,
            PrivateKeyBlob = rsaPrivateKeyBlob,
            ResumableSessionId = resumableSessionId
        };

        return new RemotingClient(config);
    }

    private static RemotingServer StartServer(
        int networkPort,
        EventHandler<Exception> onServerError)
    {
        var config = new ServerConfig()
        {
            IsDefault = false,
            UniqueServerInstanceName = $"SessionResumeNoAuthTestServer_{networkPort}",
            MessageEncryption = false,
            //Channel = new WebsocketServerChannel(), // FIXME
            NetworkPort = networkPort,
            AuthenticationRequired = false,
            RegisterServicesAction = container =>
            {
                var testService = new TestService()
                {
                    TestMethodFake = arg => arg
                };

                container.RegisterService<ITestService>(
                    factoryDelegate: () => testService,
                    lifetime: ServiceLifetime.Singleton);
            }
        };

        var server = new RemotingServer(config);
        server.Error += onServerError;
        server.Start();

        // wait until the channel is actually listening
        var deadline = DateTime.Now.AddSeconds(10);
        while (!server.Channel.IsListening && DateTime.Now < deadline)
            Thread.Sleep(20);

        if (!server.Channel.IsListening)
            throw new Exception($"Server on port {networkPort} did not start listening in time.");

        return server;
    }
}
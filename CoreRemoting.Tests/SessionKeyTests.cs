using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests;

[Collection("CoreRemoting")]
public class SessionKeyTests
{
    private const int ServerPort_NewMode = 9201;
    private const int ServerPort_LegacyMode = 9202;

    [Fact]
    public async Task Client_should_use_random_session_key_for_message_encryption()
    {
        var serverErrorCount = 0;
        Exception lastServerError = null;

        var server =
            StartEncryptedServer(
                useLegacySessionKeyDerivation: false,
                networkPort: ServerPort_NewMode,
                onServerError: (s, ex) =>
                {
                    Interlocked.Increment(ref serverErrorCount);
                    lastServerError = ex;
                });

        try
        {
            using var client =
                new RemotingClient(new ClientConfig()
                {
                    ConnectionTimeout = 0,
                    MessageEncryption = true,
                    KeySize = 1024,
                    ServerPort = server.Config.NetworkPort
                });

            client.Connect();

            Assert.True(client.HasSession);
            Assert.Single(server.SessionRepository.Sessions);
            var session = server.SessionRepository.Sessions.Single();

            // Server must use a random session key which differs from the session ID
            Assert.NotNull(session.SharedSecret);
            Assert.Equal(32, session.SharedSecret.Length);
            Assert.False(
                session.SessionId.ToByteArray().SequenceEqual(session.SharedSecret),
                "Session key must not be derived from the session ID");

            var proxy = client.CreateProxy<ITestService>();

            // RPC works with randomly exchanged session key on both sides
            Assert.Equal("test", proxy.TestMethod("test"));
        }
        finally
        {
            // Allow graceful goodbye/dispose to complete to verify the server accepted the encrypted farewell
            await Task.Delay(500);

            if (lastServerError != null)
                throw new Exception($"Unexpected server error: {lastServerError}");

            Assert.Equal(0, serverErrorCount);

            server.Stop();
        }
    }

    [Fact]
    public async Task Client_should_connect_to_server_using_legacy_session_key_derivation()
    {
        var serverErrorCount = 0;
        Exception lastServerError = null;

        var server =
            StartEncryptedServer(
                useLegacySessionKeyDerivation: true,
                networkPort: ServerPort_LegacyMode,
                onServerError: (s, ex) =>
                {
                    Interlocked.Increment(ref serverErrorCount);
                    lastServerError = ex;
                });

        try
        {
            using var client =
                new RemotingClient(new ClientConfig()
                {
                    ConnectionTimeout = 0,
                    MessageEncryption = true,
                    KeySize = 1024,
                    ServerPort = server.Config.NetworkPort
                });

            client.Connect();

            Assert.True(client.HasSession);

            var session = server.SessionRepository.Sessions.Single();

            // Legacy mode: shared secret must be derived from the session ID
            Assert.Equal(session.SessionId.ToByteArray(), session.SharedSecret);

            // Client falls back to sessionId-derived shared secret and stays compatible
            var proxy = client.CreateProxy<ITestService>();

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

    private static RemotingServer StartEncryptedServer(
        bool useLegacySessionKeyDerivation,
        int networkPort,
        EventHandler<Exception> onServerError)
    {
        var config =
            new ServerConfig()
            {
                IsDefault = false,
                UniqueServerInstanceName = $"EncryptionTestServer_{networkPort}",
                MessageEncryption = true,
                UseLegacySessionKeyDerivation = useLegacySessionKeyDerivation,
                KeySize = 1024,
                NetworkPort = networkPort,
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

        return server;
    }
}

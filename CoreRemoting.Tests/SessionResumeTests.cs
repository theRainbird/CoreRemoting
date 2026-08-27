using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Authentication;
using CoreRemoting.Authentication.SecureRemotePassword;
using CoreRemoting.Channels;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using SecureRemotePassword;
using Xunit;

namespace CoreRemoting.Tests;

using static SrpProtocolConstants;

[Collection("CoreRemoting")]
public class SessionResumeTests
{
    private const string UserName = "bozo";
    private const string Password = "h4ck3r";

    // custom port range for these tests (9095+ is used by other test classes)
    private static int _nextPort = 9300;

    // SRP-6a parameters matching the existing SRP tests
    private static readonly SrpParameters SrpParametersInstance = SrpParameters.Create<SHA384>(@"
        FFFFFFFF FFFFFFFF C90FDAA2 2168C234 C4C6628B 80DC1CD1 29024E08
        8A67CC74 020BBEA6 3B139B22 514A0879 8E3404DD EF9519B3 CD3A431B
        302B0A6D F25F1437 4FE1356D 6D51C245 E485B576 625E7EC6 F44C42E9
        A637ED6B 0BFF5CB6 F406B7ED EE386BFB 5A899FA5 AE9F2411 7C4B1FE6
        49286651 ECE45B3D C2007CB8 A163BF05 98DA4836 1C55D39A 69163FA8
        FD24CF5F 83655D23 DCA3AD96 1C62F356 208552BB 9ED52907 7096966D
        670C354E 4ABC9804 F1746C08 CA18217C 32905E46 2E36CE3B E39E772C
        180E8603 9B2783A2 EC07A28F B5C55DF0 6F4C52C9 DE2BCBF6 95581718
        3995497C EA956AE5 15D22618 98FA0510 15728E5A 8AAAC42D AD33170D
        04507A33 A85521AB DF1CBA64 ECFB8504 58DBEF0A 8AEA7157 5D060C7D
        B3970F85 A6E1E4C7 ABF5AE8C DB0933D7 1E8C94E0 4A25619D CEE3D226
        1AD2EE6B F12FFA06 D98A0864 D8760273 3EC86A64 521F2B18 177B200C
        BBE11757 7A615D6C 770988C0 BAD946E2 08E24FA0 74E5AB31 43DB5BFC
        E0FD108E 4B82D120 A93AD2CA FFFFFFFF FFFFFFFF", "05");

    [Fact]
    public async Task Client_should_resume_parked_session_after_abrupt_disconnect()
    {
        var serverErrorCount = 0;
        Exception lastServerError = null;
        int networkPort = Interlocked.Increment(ref _nextPort);

        var server = StartServer(
            networkPort: networkPort,
            authenticationRequired: true,
            onServerError: (s, ex) =>
            {
                Interlocked.Increment(ref serverErrorCount);
                lastServerError = ex;
            });

        try
        {
            using var client = CreateClient(networkPort, authenticationRequired: true);
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

            // re-authentication ran automatically and RPC works again with the same key material
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
            authenticationRequired: true,
            onServerError: (s, ex) =>
            {
                Interlocked.Increment(ref serverErrorCount);
                lastServerError = ex;
            });

        try
        {
            using (var firstClient = CreateClient(networkPort, authenticationRequired: true))
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
                    authenticationRequired: true,
                    rsaPrivateKeyBlob: privateKeyBlob,
                    resumableSessionId: sessionId);

                await secondClient.ConnectAsync();

                Assert.Equal(sessionId, secondClient.SessionId);
                Assert.Single(server.SessionRepository.Sessions);

                var resumed = server.SessionRepository.Sessions.Single();

                Assert.False(resumed.IsParked);
                Assert.Equal(sessionId.Value, resumed.SessionId);

                // re-authentication ran automatically and RPC works again
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
            authenticationRequired: false,
            onServerError: (s, ex) =>
            {
                Interlocked.Increment(ref serverErrorCount);
                lastServerError = ex;
            });

        try
        {
            Guid sessionId;

            using (var firstClient = CreateClient(networkPort, authenticationRequired: false))
            {
                await firstClient.ConnectAsync();
                sessionId = client_GetSessionId(firstClient);
                await HardKill(firstClient);
            }

            // wrong RSA key + stale session ID: the server has to reject the resume attempt
            using var secondClient = CreateClient(
                networkPort,
                authenticationRequired: false,
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
            authenticationRequired: false,
            onServerError: (s, ex) =>
            {
                Interlocked.Increment(ref serverErrorCount);
                lastServerError = ex;
            });

        try
        {
            Guid sessionId;

            using (var firstClient = CreateClient(networkPort, authenticationRequired: false))
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
                authenticationRequired: false,
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
        bool authenticationRequired,
        byte[] rsaPrivateKeyBlob = null,
        Guid? resumableSessionId = null)
    {
        var config = new ClientConfig()
        {
            ConnectionTimeout = 0,
            MessageEncryption = true,
            KeySize = 1024,
            ServerHostName = "localhost",
            ServerPort = serverPort,
            KeepSessionAliveInterval = 0,
            PrivateKeyBlob = rsaPrivateKeyBlob,
            ResumableSessionId = resumableSessionId
        };

        if (authenticationRequired)
        {
            config.Authenticator = new SrpAuthenticator(SrpParametersInstance);
            config.Credentials =
            [
                new() { Name = USERNAME, Value = UserName },
                new() { Name = PASSWORD, Value = Password }
            ];
        }

        return new RemotingClient(config);
    }

    private static RemotingServer StartServer(
        int networkPort,
        bool authenticationRequired,
        EventHandler<Exception> onServerError)
    {
        var config = new ServerConfig()
        {
            IsDefault = false,
            UniqueServerInstanceName = $"SessionResumeTestServer_{networkPort}",
            MessageEncryption = true,
            KeySize = 1024,
            NetworkPort = networkPort,
            AuthenticationRequired = authenticationRequired,
            AuthenticationProvider = authenticationRequired
                ? new SrpAuthenticationProvider(new SampleAccountRepository(), SrpParametersInstance)
                : null,
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

        // wait until the channel is actually listening (WatsonTcp binds asynchronously)
        var deadline = DateTime.Now.AddSeconds(10);
        while (!server.Channel.IsListening && DateTime.Now < deadline)
            Thread.Sleep(20);

        if (!server.Channel.IsListening)
            throw new Exception($"Server on port {networkPort} did not start listening in time.");

        return server;
    }

    private class SampleAccountRepository : ISrpAccountRepository
    {
        private readonly SrpAccount _sampleAccount;

        public SampleAccountRepository()
        {
            var srpClient = new SrpClient(SrpParametersInstance);
            var salt = srpClient.GenerateSalt();
            var privateKey = srpClient.DerivePrivateKey(salt, UserName, Password);
            var verifier = srpClient.DeriveVerifier(privateKey);
            _sampleAccount = new SrpAccount
            {
                UserName = UserName,
                Salt = salt,
                Verifier = verifier
            };
        }

        public Task<ISrpAccount> FindByName(string userName)
        {
            if (_sampleAccount.UserName == userName)
                return Task.FromResult<ISrpAccount>(_sampleAccount);

            return Task.FromResult<ISrpAccount>(null);
        }

        public Task<RemotingIdentity> GetIdentity(ISrpAccount account)
        {
            return Task.FromResult(new RemotingIdentity
            {
                Name = account.UserName,
                IsAuthenticated = true
            });
        }

        private class SrpAccount : ISrpAccount
        {
            public string UserName { get; init; }
            public string Salt { get; init; }
            public string Verifier { get; init; }
        }
    }
}

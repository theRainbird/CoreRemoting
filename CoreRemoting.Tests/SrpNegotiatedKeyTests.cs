using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Authentication;
using CoreRemoting.Authentication.SecureRemotePassword;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using SecureRemotePassword;
using Xunit;

namespace CoreRemoting.Tests;

using static SrpProtocolConstants;

[Collection("CoreRemoting")]
public class SrpNegotiatedKeyTests
{
    private const string UserName = "bozo";
    private const string Password = "h4ck3r";

    private const int ServerPort_Negotiated = 9203;
    private const int ServerPort_Default = 9204;
    private const int ServerPort_LegacyNegotiated = 9205;

    // Custom SRP-6a parameters (3072-bit group from RFC5054, SHA-384 hash => 48 byte session key)
    private static readonly SrpParameters CustomSrpParameters = SrpParameters.Create<SHA384>(@"
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
    public async Task Client_and_server_should_rekey_session_with_srp_key_after_authentication()
    {
        var serverErrorCount = 0;
        Exception lastServerError = null;

        var server = StartSrpServer(
            useNegotiatedSessionKey: true,
            useLegacySessionKeyDerivation: false,
            networkPort: ServerPort_Negotiated,
            onServerError: (s, ex) =>
            {
                Interlocked.Increment(ref serverErrorCount);
                lastServerError = ex;
            });

        try
        {
            using var client = CreateClient(ServerPort_Negotiated);

            // use negotiated key as is
            server.Config.HkdfProvider = 
                client.Config.HkdfProvider = 
                    Hkdf.Bypass;

            await client.ConnectAsync();

            Assert.True(client.HasSession);
            Assert.Single(server.SessionRepository.Sessions);

            var session = server.SessionRepository.Sessions.Single();

            // Server must have re-keyed the session with the SRP session key (SHA-384 => 48 bytes)
            Assert.NotNull(session.SharedSecret);
            Assert.Equal(CustomSrpParameters.HashSizeBytes, session.SharedSecret.Length);
            Assert.False(
                session.SessionId.ToByteArray().SequenceEqual(session.SharedSecret),
                "Re-keyed session must not use the session ID anymore");

            // RPC with message encryption works after re-keying on both sides
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

    [Fact]
    public async Task Server_should_keep_random_handshake_key_when_negotiation_is_disabled()
    {
        var serverErrorCount = 0;
        Exception lastServerError = null;

        var server = StartSrpServer(
            useNegotiatedSessionKey: false,
            useLegacySessionKeyDerivation: false,
            networkPort: ServerPort_Default,
            onServerError: (s, ex) =>
            {
                Interlocked.Increment(ref serverErrorCount);
                lastServerError = ex;
            });

        try
        {
            using var client = CreateClient(ServerPort_Default);

            await client.ConnectAsync();

            Assert.True(client.HasSession);

            var session = server.SessionRepository.Sessions.Single();

            // no negotiated key: random 32 byte session key from the handshake is kept
            Assert.NotNull(session.SharedSecret);
            Assert.Equal(server.Config.SharedKeySize, session.SharedSecret.Length * 8);
            Assert.False(
                session.SessionId.ToByteArray().SequenceEqual(session.SharedSecret));

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

    [Fact]
    public async Task Legacy_server_should_strip_negotiated_key_and_stay_consistent()
    {
        var serverErrorCount = 0;
        Exception lastServerError = null;

        var server = StartSrpServer(
            useNegotiatedSessionKey: true,
            useLegacySessionKeyDerivation: true,
            networkPort: ServerPort_LegacyNegotiated,
            onServerError: (s, ex) =>
            {
                Interlocked.Increment(ref serverErrorCount);
                lastServerError = ex;
            });

        try
        {
            using var client = CreateClient(ServerPort_LegacyNegotiated);

            await client.ConnectAsync();

            Assert.True(client.HasSession);

            var session = server.SessionRepository.Sessions.Single();

            // A legacy server cannot re-key, so the negotiated key is stripped from the
            // response and both endpoints stay on the legacy session ID derived secret
            Assert.Equal(session.SessionId.ToByteArray(), session.SharedSecret);

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

    private static RemotingClient CreateClient(int serverPort) => new(new ClientConfig()
    {
        ConnectionTimeout = 0,
        MessageEncryption = true,
        KeySize = 1024,
        ServerHostName = "localhost",
        ServerPort = serverPort,
        Authenticator = new SrpAuthenticator(CustomSrpParameters),
        Credentials =
        [
            new() { Name = USERNAME, Value = UserName },
            new() { Name = PASSWORD, Value = Password }
        ]
    });

    private static RemotingServer StartSrpServer(
        bool useNegotiatedSessionKey,
        bool useLegacySessionKeyDerivation,
        int networkPort,
        EventHandler<Exception> onServerError)
    {
        var config = new ServerConfig()
        {
            IsDefault = false,
            UniqueServerInstanceName = $"SrpNegotiatedKeyTestServer_{networkPort}",
            MessageEncryption = true,
            UseLegacySessionKeyDerivation = useLegacySessionKeyDerivation,
            KeySize = 1024,
            NetworkPort = networkPort,
            AuthenticationRequired = true,
            AuthenticationProvider = new SrpAuthenticationProvider(
                new SampleAccountRepository(),
                CustomSrpParameters,
                useNegotiatedSessionKey),
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
            var srpClient = new SrpClient(CustomSrpParameters);
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

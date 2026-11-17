using System;
using System.Security;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CoreRemoting.Authentication;
using CoreRemoting.Authentication.SecureRemotePassword;
using SecureRemotePassword;
using Xunit;

namespace CoreRemoting.Tests;

using static SrpProtocolConstants;

public class SrpAuthenticationTests : IAsyncLifetime
{
    private const string UserName = "bozo";
    private const string Password = "h4ck3r";
    private const int ServerPort = 9192;

    // Custom SRP-6a parameters (3072-bit group from RFC5054)
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

    private readonly SampleAccountRepository _repository = new();
    private RemotingServer _server;

    public async Task InitializeAsync()
    {
        // Start server once for all tests
        _server = new RemotingServer(new()
        {
            HostName = "localhost",
            NetworkPort = ServerPort,
            MessageEncryption = false,
            AuthenticationProvider = new SrpAuthenticationProvider(_repository, CustomSrpParameters),
            AuthenticationRequired = true,
            RegisterServicesAction = container =>
                container.RegisterService<ISampleService, SampleService>()
        });

        _server.Start();

        // Warmup
        await Task.Delay(100);
    }

    public Task DisposeAsync()
    {
        if (_server != null)
        {
            _server.Stop();
            _server.Dispose();
            _server = null;
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task UnknownUsernameReturnsSameSaltAndNewEphemeralOnEachRequest()
    {
        var authProvider = new SrpAuthenticationProvider(_repository, CustomSrpParameters);
        var srpClient = new SrpClient(CustomSrpParameters);
        var sessionId = Guid.NewGuid().ToString();
        var clientEphemeral = srpClient.GenerateEphemeral();

        // Simulate step 1 authentication request
        var authRequest = new AuthenticationRequestMessage
        {
            Credentials =
            [
                new() { Name = USERNAME, Value = "UnknownUser" },
                new() { Name = CLIENT_EPHEMERAL_PUBLIC, Value = clientEphemeral.Public },
                new() { Name = OPTIONAL_SESSION_ID, Value = sessionId },
            ]
        };

        var response1 = await authProvider.Authenticate(authRequest);

        Assert.NotNull(response1);
        Assert.False(response1.IsCompleted);
        Assert.False(response1.IsAuthenticated);
        Assert.NotNull(response1.Parameters);

        var salt1 = response1[SALT];
        var ephemeral1 = response1[SERVER_EPHEMERAL_PUBLIC];
        Assert.NotNull(salt1);
        Assert.NotNull(ephemeral1);

        var response2 = await authProvider.Authenticate(authRequest);

        Assert.NotNull(response2);
        var salt2 = response2[SALT];
        var ephemeral2 = response2[SERVER_EPHEMERAL_PUBLIC];

        // Same salt for unknown user, but different ephemeral
        Assert.Equal(salt1, salt2);
        Assert.NotEqual(ephemeral1, ephemeral2);
    }

    [Fact]
    public async Task AuthenticationProviderSetsAuthenticatedIdentity()
    {
        var authProvider = new SrpAuthenticationProvider(_repository, CustomSrpParameters);
        var srpClient = new SrpClient(CustomSrpParameters);
        var sessionId = Guid.NewGuid().ToString();
        var clientEphemeral = srpClient.GenerateEphemeral();

        // Step 1
        var authRequest = new AuthenticationRequestMessage
        {
            Credentials =
            [
                new() { Name = USERNAME, Value = UserName },
                new() { Name = CLIENT_EPHEMERAL_PUBLIC, Value = clientEphemeral.Public },
                new() { Name = OPTIONAL_SESSION_ID, Value = sessionId },
            ]
        };

        var response1 = await authProvider.Authenticate(authRequest);

        Assert.NotNull(response1);
        Assert.False(response1.IsCompleted);
        Assert.False(response1.IsAuthenticated);
        Assert.NotNull(response1.Parameters);

        var salt = response1[SALT];
        var serverEphemeralPublic = response1[SERVER_EPHEMERAL_PUBLIC];
        Assert.NotNull(salt);
        Assert.NotNull(serverEphemeralPublic);

        // Step 2
        var privateKey = srpClient.DerivePrivateKey(salt, UserName, Password);
        var clientSession = srpClient.DeriveSession(
            clientEphemeral.Secret,
            serverEphemeralPublic,
            salt,
            UserName,
            privateKey);

        var authRequest2 = new AuthenticationRequestMessage
        {
            Credentials =
            [
                new() { Name = CLIENT_SESSION_PROOF, Value = clientSession.Proof },
                new() { Name = OPTIONAL_SESSION_ID, Value = sessionId },
            ]
        };

        var response2 = await authProvider.Authenticate(authRequest2);

        Assert.NotNull(response2);
        Assert.True(response2.IsCompleted);
        Assert.True(response2.IsAuthenticated);
        Assert.NotNull(response2.AuthenticatedIdentity);
        Assert.Equal(UserName, response2.AuthenticatedIdentity.Name);

        var serverProof = response2[SERVER_SESSION_PROOF];
        Assert.NotNull(serverProof);

        srpClient.VerifySession(clientEphemeral.Public, clientSession, serverProof);
    }

    [Fact]
    public async Task ValidLoginUsingTcpChannel()
    {
        using var client = CreateClient(UserName, Password);
        await client.ConnectAsync();

        var proxy = client.CreateProxy<ISampleService>();
        var result = proxy.Echo("Hello");
        Assert.Equal("Hello", result);

        await client.DisposeAsync();

        // Reconnect using same credentials
        using var client2 = CreateClient(UserName, Password);
        await client2.ConnectAsync();

        var proxy2 = client2.CreateProxy<ISampleService>();
        result = proxy2.Echo("World");
        Assert.Equal("World", result);
    }

    [Fact]
    public async Task InvalidLogin_NoAuthenticator()
    {
        using var client = new RemotingClient(new()
        {
            ServerHostName = "localhost",
            ServerPort = ServerPort,
            MessageEncryption = false
            // No Authenticator
        });

        // fail on connection
        await Assert.ThrowsAsync<SecurityException>(client.ConnectAsync);
    }

    [Fact]
    public async Task InvalidLogin_WrongUsername()
    {
        var client = CreateClient(UserName + "1", Password);
        await Assert.ThrowsAsync<SecurityException>(client.ConnectAsync);
    }

    [Fact]
    public async Task InvalidLogin_WrongPassword()
    {
        var client = CreateClient(UserName, Password + "1");
        await Assert.ThrowsAsync<SecurityException>(client.ConnectAsync);
    }

    [Fact]
    public async Task AuthenticationStep1WasNotPerformed()
    {
        // This test simulates a broken authenticator that skips step 1
        var brokenAuthenticator = new BrokenSrpAuthenticator();
        var client = new RemotingClient(new()
        {
            ServerHostName = "localhost",
            ServerPort = ServerPort,
            MessageEncryption = false,
            Authenticator = brokenAuthenticator
        });

        await Assert.ThrowsAsync<SecurityException>(client.ConnectAsync);
    }

    private RemotingClient CreateClient(string username, string password)
    {
        return new RemotingClient(new()
        {
            ServerHostName = "localhost",
            ServerPort = ServerPort,
            MessageEncryption = false,
            Authenticator = new SrpAuthenticator(CustomSrpParameters),
            Credentials =
            [
                new() { Name = USERNAME, Value = username },
                new() { Name = PASSWORD, Value = password }
            ]
        });
    }

    // Sample repository implementation
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

    // Sample service interface and implementation
    public interface ISampleService
    {
        string Echo(string message);
    }

    public class SampleService : ISampleService
    {
        public string Echo(string message) => message;
    }

    // Broken authenticator for test purposes
    private class BrokenSrpAuthenticator : IAuthenticator
    {
        public async Task<AuthenticationResponseMessage> Authenticate(Credential[] credentials, IAuthenticationProvider authProxy)
        {
            // Skip step 1, directly send step 2 with fake proof
            await authProxy.Authenticate(new AuthenticationRequestMessage
            {
                Credentials =
                [
                    new() { Name = CLIENT_SESSION_PROOF, Value = "woof" }
                ]
            });

            return new();
        }
    }
}

using System;
using System.Security;
using System.Threading.Tasks;
using CoreRemoting.Authentication;
using CoreRemoting.Authentication.JPake;
using Xunit;

namespace CoreRemoting.Tests;

using static JPakeProtocolConstants;

public class JPakeAuthenticationTests : IAsyncLifetime
{
    private const string UserName = "bozo";
    private const string Password = "h4ck3r";
    private const int ServerPort = 9192;

    private readonly SampleAccountRepository _repository = new();
    private RemotingServer _serverPlain;

    public Task InitializeAsync()
    {
        _serverPlain = new RemotingServer(new()
        {
            HostName = "localhost",
            NetworkPort = ServerPort,
            MessageEncryption = false,
            AuthenticationProvider = new JPakeAuthenticationProvider(_repository),
            AuthenticationRequired = true,
            RegisterServicesAction = container =>
                container.RegisterService<ISampleService, SampleService>()
        });

        _serverPlain.Start();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (_serverPlain != null)
        {
            _serverPlain.Stop();
            _serverPlain.Dispose();
            _serverPlain = null;
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task AuthenticationWithWrongPasswordFails()
    {
        var authProvider = new JPakeAuthenticationProvider(_repository);
        var authenticator = new JPakeAuthenticator();

        var credentials = new[]
        {
            new Credential { Name = USERNAME, Value = UserName },
            new Credential { Name = PASSWORD, Value = Password + "1" },
            new Credential { Name = OPTIONAL_SESSION_ID, Value = Guid.NewGuid().ToString() },
        };

        await Assert.ThrowsAsync<SecurityException>(async () =>
            await authenticator.Authenticate(credentials, authProvider));
    }

    [Fact]
    public async Task UnknownUsernameFailsToAuthenticate()
    {
        var authProvider = new JPakeAuthenticationProvider(_repository);
        var authenticator = new JPakeAuthenticator();

        // Attempt to authenticate with unknown user
        // Provider uses a fake password, so authentication will fail at Round 3
        var credentials = new[]
        {
            new Credential { Name = USERNAME, Value = UserName + "1" },
            new Credential { Name = PASSWORD, Value = Password },
            new Credential { Name = OPTIONAL_SESSION_ID, Value = Guid.NewGuid().ToString() },
        };

        // Expect SecurityException due to MAC mismatch in Round 3
        await Assert.ThrowsAsync<SecurityException>(async () =>
            await authenticator.Authenticate(credentials, authProvider));
    }

    [Fact]
    public async Task AuthenticationProviderSetsAuthenticatedIdentity()
    {
        var authProvider = new JPakeAuthenticationProvider(_repository);
        var authenticator = new JPakeAuthenticator();

        var credentials = new[]
        {
            new Credential { Name = USERNAME, Value = UserName },
            new Credential { Name = PASSWORD, Value = Password },
            new Credential { Name = OPTIONAL_SESSION_ID, Value = Guid.NewGuid().ToString() },
        };

        // Full authentication via public API
        var response = await authenticator.Authenticate(credentials, authProvider);

        Assert.NotNull(response);
        Assert.True(response.IsCompleted);
        Assert.True(response.IsAuthenticated);
        Assert.NotNull(response.AuthenticatedIdentity);
        Assert.Equal(UserName, response.AuthenticatedIdentity.Name);
        Assert.NotNull(response.NegotiatedSharedKey);
        Assert.NotNull(response.NegotiatedSharedKey.InputKeyMaterial);
        Assert.NotEmpty(response.NegotiatedSharedKey.InputKeyMaterial);
    }

    [Fact]
    public async Task ValidLoginUsingTcpChannel_EncryptionDisabled()
    {
        using var client = CreateClient(UserName, Password);
        await client.ConnectAsync();

        var proxy = client.CreateProxy<ISampleService>();
        var result = proxy.Echo("Hello");
        Assert.Equal("Hello", result);

        await client.DisposeAsync();

        using var client2 = CreateClient(UserName, Password);
        await client2.ConnectAsync();

        var proxy2 = client2.CreateProxy<ISampleService>();
        result = proxy2.Echo("World");
        Assert.Equal("World", result);
    }

    [Fact]
    public async Task ValidLoginUsingTcpChannel_EncryptionEnabled()
    {
        using var client = CreateClient(UserName, Password, encryption: true);
        await client.ConnectAsync();

        var proxy = client.CreateProxy<ISampleService>();
        var result = proxy.Echo("Hello");
        Assert.Equal("Hello", result);

        await client.DisposeAsync();

        using var client2 = CreateClient(UserName, Password, encryption: true);
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
        });

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
    public async Task AuthenticationRound1WasNotPerformed()
    {
        var brokenAuthenticator = new BrokenJPakeAuthenticator();
        var client = new RemotingClient(new()
        {
            ServerHostName = "localhost",
            ServerPort = ServerPort,
            MessageEncryption = false,
            Authenticator = brokenAuthenticator
        });

        await Assert.ThrowsAsync<SecurityException>(client.ConnectAsync);
    }

    private RemotingClient CreateClient(string username, string password, bool encryption = false)
    {
        return new RemotingClient(new()
        {
            ServerHostName = "localhost",
            ServerPort = ServerPort,
            MessageEncryption = encryption,
            Authenticator = new JPakeAuthenticator(),
            Credentials =
            [
                new() { Name = USERNAME, Value = username },
                new() { Name = PASSWORD, Value = password }
            ]
        });
    }

    private class SampleAccountRepository : IJPakeAccountRepository
    {
        private readonly JPakeAccount _sampleAccount;

        public SampleAccountRepository()
        {
            _sampleAccount = new JPakeAccount
            {
                UserName = UserName,
                Password = Password
            };
        }

        public Task<IJPakeAccount> FindByName(string userName)
        {
            if (_sampleAccount.UserName == userName)
                return Task.FromResult<IJPakeAccount>(_sampleAccount);

            return Task.FromResult<IJPakeAccount>(null);
        }

        public Task<RemotingIdentity> GetIdentity(IJPakeAccount account)
        {
            return Task.FromResult(new RemotingIdentity
            {
                Name = account.UserName,
                IsAuthenticated = true
            });
        }

        private class JPakeAccount : IJPakeAccount
        {
            public string UserName { get; init; }
            public string Password { get; init; }
        }
    }

    public interface ISampleService
    {
        string Echo(string message);
    }

    public class SampleService : ISampleService
    {
        public string Echo(string message) => message;
    }

    private class BrokenJPakeAuthenticator : IAuthenticator
    {
        public async Task<AuthenticationResponseMessage> Authenticate(Credential[] credentials, IAuthenticationProvider authProxy)
        {
            // Skip Round 1, directly send Round 2 with fake data
            await authProxy.Authenticate(new AuthenticationRequestMessage
            {
                Credentials =
                [
                    new() { Name = ROUND2_A, Value = "fake" },
                    new() { Name = ROUND2_PROOF_A, Value = "fake" }
                ]
            });

            return new();
        }
    }
}
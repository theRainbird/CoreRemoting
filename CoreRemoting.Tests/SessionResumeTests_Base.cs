using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Authentication;
using CoreRemoting.Authentication.SecureRemotePassword;
using CoreRemoting.Channels;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using SecureRemotePassword;
using Xunit;

namespace CoreRemoting.Tests.Sessions;

using static SrpProtocolConstants;

[Collection("CoreRemoting")]
public abstract class SessionResumeTests_Base
{
    protected const string UserName = "bozo";
    protected const string Password = "h4ck3r";

    // custom port range for these tests (9095+ is used by other test classes)
    protected static int _nextPort = 9300;

    protected virtual IServerChannel ServerChannel => null;
    protected virtual IClientChannel ClientChannel => null;
    protected virtual bool MessageEncryption => true;
    protected virtual int KeySize => 1024;
    protected virtual bool AuthenticationRequiredForResumeTests => true;
    protected virtual bool AuthenticationRequiredForNegativeTests => false;

    protected static Guid client_GetSessionId(RemotingClient client)
    {
        var sessionId = client.SessionId;
        Assert.NotNull(sessionId);
        return sessionId.Value;
    }

    protected string Shorten(string longString)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(longString));
        return Convert.ToHexString(hash);
    }

    protected RemotingClient CreateClient(
        int serverPort,
        bool authenticationRequired,
        byte[] privateKeyBlob = null,
        Guid? resumableSessionId = null,
        [CallerMemberName] string channelName = null)
    {
        var config = new ClientConfig()
        {
            Channel = ClientChannel,
            ConnectionTimeout = 30,
            MessageEncryption = MessageEncryption,
            KeySize = KeySize,
            ServerHostName = "localhost",
            ServerPort = serverPort,
            ChannelConnectionName = Shorten($"{channelName}_{GetType().Name}"),
            KeepSessionAliveInterval = 0,
            PrivateKeyBlob = privateKeyBlob,
            ResumableSessionId = resumableSessionId
        };

        if (authenticationRequired)
        {
            config.Authenticator = new SrpAuthenticator();
            config.Credentials =
            [
                new() { Name = USERNAME, Value = UserName },
                new() { Name = PASSWORD, Value = Password }
            ];
        }

        return new RemotingClient(config);
    }

    protected int serverErrorCount;

    protected Exception lastServerError;

    protected virtual void CheckServerErrorCount()
    {
        if (lastServerError != null)
            throw new Exception($"Unexpected server error: {lastServerError}");

        Assert.Equal(0, serverErrorCount);
    }

    private void OnServerError(object s, Exception ex)
    {
        Interlocked.Increment(ref serverErrorCount);

        lastServerError = ex;
    }

    protected RemotingServer StartServer(
        int networkPort,
        bool authenticationRequired,
        EventHandler<Exception> onServerError = null,
        ISessionRepository repository = null,
        [CallerMemberName] string channelName = null)
    {
        var config = new ServerConfig()
        {
            Channel = ServerChannel,
            IsDefault = false,
            UniqueServerInstanceName = $"SessionResumeTestServer_{networkPort}_{GetType().Name}",
            MessageEncryption = MessageEncryption,
            KeySize = KeySize,
            NetworkPort = networkPort,
            ChannelConnectionName = Shorten($"{channelName}_{GetType().Name}"),
            AuthenticationRequired = authenticationRequired,
            AuthenticationProvider = authenticationRequired
                ? new SrpAuthenticationProvider(new SampleAccountRepository())
                : null,
            SessionRepository = repository,
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

        serverErrorCount = 0;
        lastServerError = null;

        var server = new RemotingServer(config);
        server.Error += onServerError ?? OnServerError;
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
            var srpClient = new SrpClient();
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

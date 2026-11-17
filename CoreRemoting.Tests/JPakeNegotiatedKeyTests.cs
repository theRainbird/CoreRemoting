using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Authentication;
using CoreRemoting.Authentication.JPake;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests;

using static JPakeProtocolConstants;

[Collection("CoreRemoting")]
public class JPakeNegotiatedKeyTests
{
    private const string UserName = "bozo";
    private const string Password = "h4ck3r";

    private const int ServerPort_Negotiated = 9203;

    [Fact]
    public async Task Client_and_server_should_rekey_session_with_jpake_key_after_authentication()
    {
        var serverErrorCount = 0;
        Exception lastServerError = null;

        var server = StartJPakeServer(
            networkPort: ServerPort_Negotiated,
            onServerError: (s, ex) =>
            {
                Interlocked.Increment(ref serverErrorCount);
                lastServerError = ex;
            });

        try
        {
            using var client = CreateClient(ServerPort_Negotiated);

            await client.ConnectAsync();

            Assert.True(client.HasSession);
            Assert.Single(server.SessionRepository.Sessions);

            var session = server.SessionRepository.Sessions.Single();

            // Server must have re-keyed the session with the J-PAKE derived key
            Assert.NotNull(session.SharedSecret);
            // J-PAKE with NIST_2048 produces a key of specific length (depends on hash used)
            Assert.True(session.SharedSecret.Length > 0);
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

    private static RemotingClient CreateClient(int serverPort) => new(new ClientConfig()
    {
        ConnectionTimeout = 0,
        MessageEncryption = true,
        KeySize = 1024,
        ServerHostName = "localhost",
        ServerPort = serverPort,
        Authenticator = new JPakeAuthenticator(),
        Credentials =
        [
            new() { Name = USERNAME, Value = UserName },
            new() { Name = PASSWORD, Value = Password }
        ]
    });

    private static RemotingServer StartJPakeServer(
        int networkPort,
        EventHandler<Exception> onServerError)
    {
        var config = new ServerConfig()
        {
            IsDefault = false,
            UniqueServerInstanceName = $"JPakeNegotiatedKeyTestServer_{networkPort}",
            MessageEncryption = true,
            KeySize = 1024,
            NetworkPort = networkPort,
            AuthenticationRequired = true,
            AuthenticationProvider = new JPakeAuthenticationProvider(new SampleAccountRepository()),
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

        var deadline = DateTime.Now.AddSeconds(10);
        while (!server.Channel.IsListening && DateTime.Now < deadline)
            Thread.Sleep(20);

        if (!server.Channel.IsListening)
            throw new Exception($"Server on port {networkPort} did not start listening in time.");

        return server;
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
}
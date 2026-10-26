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
    protected override IServerChannel ServerChannel => new TcpServerChannel();
    protected override IClientChannel ClientChannel => new TcpClientChannel();
    protected override bool MessageEncryption => false;
    protected override bool AuthenticationRequiredForResumeTests => false;
    protected override bool AuthenticationRequiredForNegativeTests => false;

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Client_should_resume_session_after_server_restart()
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
}

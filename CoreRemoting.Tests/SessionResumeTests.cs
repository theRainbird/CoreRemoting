using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests.Sessions;
public partial class SessionResumeTests : SessionResumeTests_Base
{
    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Client_should_resume_parked_session_after_abrupt_disconnect()
    {
        using var ctx = ValidationSyncContext.Install();

        int networkPort = Interlocked.Increment(ref _nextPort);

        var server = StartServer(networkPort, AuthenticationRequiredForResumeTests);

        try
        {
            using var client = CreateClient(networkPort, AuthenticationRequiredForResumeTests);

            await client.ConnectAsync()
                .ConfigureAwait(false);

            Assert.True(client.HasSession);

            Guid? originalSessionId = client.SessionId;
            Assert.NotNull(originalSessionId);

            var proxy = client.CreateProxy<ITestService>();
            Assert.Equal("test", proxy.TestMethod("test"));

            // abruptly kill the transport (no goodbye message, session must be parked on server)
            await client.HardKill()
                .ConfigureAwait(false);

            // parked sessions stay in the repository until swept by inactivity
            Assert.Single(server.SessionRepository.Sessions);
            Assert.True(
                server.SessionRepository.Sessions.Single().IsParked,
                "Session should be parked after an abrupt disconnect");

            // reconnect the same client instance (same RSA key pair, session ID still known)
            await client.ConnectAsync()
                .ConfigureAwait(false);

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
            await Task.Delay(500)
                .ConfigureAwait(false);

            CheckServerErrorCount();

            server.Stop();
        }
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Client_with_persisted_key_material_should_resume_session_after_reinstantiation()
    {
        using var ctx = ValidationSyncContext.Install();

        int networkPort = Interlocked.Increment(ref _nextPort);

        var server = StartServer(networkPort, AuthenticationRequiredForResumeTests);

        try
        {
            using (var firstClient = CreateClient(networkPort, AuthenticationRequiredForResumeTests))
            {
                await firstClient.ConnectAsync()
                    .ConfigureAwait(false);

                Guid? sessionId = firstClient.SessionId;
                byte[] privateKeyBlob = firstClient.PrivateKey;

                Assert.NotNull(sessionId);
                Assert.NotNull(privateKeyBlob);

                // abruptly disconnect so the session is parked and keeps its key material
                await firstClient.HardKill()
                    .ConfigureAwait(false);

                // simulate process restart: the app persisted session ID + client RSA private key,
                // a new client instance must resume the same session with the same identity
                using var secondClient = CreateClient(networkPort,
                    AuthenticationRequiredForResumeTests,
                    privateKeyBlob: privateKeyBlob,
                    resumableSessionId: sessionId);

                await secondClient.ConnectAsync()
                    .ConfigureAwait(false);

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
            await Task.Delay(500)
                .ConfigureAwait(false);

            CheckServerErrorCount();

            server.Stop();
        }
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Client_with_mismatching_key_material_should_not_resume_parked_session()
    {
        using var ctx = ValidationSyncContext.Install();

        int networkPort = Interlocked.Increment(ref _nextPort);

        var server = StartServer(networkPort, AuthenticationRequiredForNegativeTests);

        try
        {
            Guid sessionId;

            using (var firstClient = CreateClient(networkPort,
                AuthenticationRequiredForNegativeTests))
            {
                await firstClient.ConnectAsync()
                    .ConfigureAwait(false);

                sessionId = client_GetSessionId(firstClient);

                await firstClient.HardKill()
                    .ConfigureAwait(false);
            }

            // wrong RSA key + stale session ID: the server has to reject the resume attempt
            using var secondClient = CreateClient(networkPort,
                AuthenticationRequiredForNegativeTests,
                resumableSessionId: sessionId);

            var exception =
                await Assert.ThrowsAsync<RemotingException>(secondClient.ConnectAsync)
                    .ConfigureAwait(false);

            Assert.Contains("refused to resume", exception.Message);

            // the rejected resume attempt led to a NEW session; the parked one is untouched
            Assert.Equal(2, server.SessionRepository.Sessions.Count());
            Assert.Contains(server.SessionRepository.Sessions, s => s.IsParked);
        }
        finally
        {
            await Task.Delay(500)
                .ConfigureAwait(false);

            CheckServerErrorCount();

            server.Stop();
        }
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Resumable_session_id_should_fail_when_session_was_disposed_gently()
    {
        using var ctx = ValidationSyncContext.Install();

        int networkPort = Interlocked.Increment(ref _nextPort);

        var server = StartServer(networkPort, AuthenticationRequiredForNegativeTests);

        try
        {
            Guid sessionId;

            using (var firstClient = CreateClient(networkPort,
                AuthenticationRequiredForNegativeTests))
            {
                await firstClient.ConnectAsync()
                    .ConfigureAwait(false);

                sessionId = client_GetSessionId(firstClient);

                // graceful disconnect sends a goodbye message and disposes the session (no parking)
                await firstClient.DisposeAsync()
                    .ConfigureAwait(false);

                await Task.Delay(500)
                    .ConfigureAwait(false);

                Assert.Empty(server.SessionRepository.Sessions);
            }

            using var secondClient = CreateClient(networkPort,
                AuthenticationRequiredForNegativeTests,
                resumableSessionId: sessionId);

            // session no longer exists, so the server created a new one and the strict check fails
            var exception =
                await Assert.ThrowsAsync<RemotingException>(secondClient.ConnectAsync)
                    .ConfigureAwait(false);

            Assert.Contains("refused to resume", exception.Message);
        }
        finally
        {
            await Task.Delay(500)
                .ConfigureAwait(false);

            CheckServerErrorCount();

            server.Stop();
        }
    }
}

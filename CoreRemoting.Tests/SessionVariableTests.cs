using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Authentication;
using CoreRemoting.Channels;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests;

[Collection("CoreRemoting")]
public class SessionVariableTests
{
    // custom port range for these tests (9095+/9300+ ranges are used by other test classes)
    private static int _nextPort = 9400;

    [Fact]
    public async Task Session_variables_should_be_accessible_from_within_the_remoting_context()
    {
        var serverErrorCount = 0;
        Exception lastServerError = null;
        int networkPort = Interlocked.Increment(ref _nextPort);

        RemotingSession capturedSession = null;

        var server = StartServer(
            networkPort: networkPort,
            testMethodFake: arg =>
            {
                // service code has access to the ambient session and can populate / read variables
                var session = RemotingSession.Current;

                if (session == null)
                    return "NoAmbientSession";

                capturedSession = session;

                session.SetVariable("Elevated", true);
                session.SetVariable("Counter", 42);

                return $"{session.GetVariable<bool>("Elevated")};{session.GetVariable<int>("Counter")};{session.GetVariable<string>("Missing")}";
            },
            onServerError: (s, ex) =>
            {
                Interlocked.Increment(ref serverErrorCount);
                lastServerError = ex;
            });

        try
        {
            using var client = CreateClient(networkPort);
            await client.ConnectAsync();

            // the fake encodes: elevated value, counter value and missing variable (null renders empty)
            var proxy = client.CreateProxy<ITestService>();
            Assert.Equal("True;42;", proxy.TestMethod("x"));

            var session = server.SessionRepository.Sessions.Single();
            Assert.NotNull(capturedSession);
            Assert.Equal(session.SessionId, capturedSession.SessionId);

            // direct API checks on the live session
            Assert.True(session.HasVariable("Elevated"));
            Assert.Equal(42, session.GetVariable<int>("Counter"));
            Assert.False(session.TryGetVariable<string>("Counter", out var wrongTypeCast));
            Assert.Null(wrongTypeCast);
            Assert.True(session.RemoveVariable("Elevated"));
            Assert.False(session.HasVariable("Elevated"));

            // setting a null value removes the variable
            session.SetVariable("Temporary", "value");
            session.SetVariable("Temporary", null);
            Assert.False(session.HasVariable("Temporary"));

            // missing variables return default(T)
            Assert.Equal(0, session.GetVariable<int>("DoesNotExist"));

            // incompatible types throw explicitly
            Assert.Throws<InvalidCastException>(() => session.GetVariable<string>("Counter"));

            // null names are rejected
            Assert.Throws<ArgumentNullException>(() => session.SetVariable(null, "value"));
            Assert.False(session.HasVariable(null));

            // the snapshot property returns a point in time copy
            session.SetVariable("Snapshot", "check");
            var snapshot = session.Variables;
            Assert.Equal(2, snapshot.Count);
            Assert.Contains("Snapshot", snapshot.Keys);
            Assert.Contains("Counter", snapshot.Keys);

            session.ClearVariables();
            Assert.Empty(session.Variables);
        }
        finally
        {
            if (lastServerError != null)
                throw new Exception($"Unexpected server error: {lastServerError}");

            Assert.Equal(0, serverErrorCount);

            server.Stop();
        }
    }

    [Fact]
    public async Task Authentication_provider_should_able_to_populate_session_variables()
    {
        var serverErrorCount = 0;
        Exception lastServerError = null;
        int networkPort = Interlocked.Increment(ref _nextPort);

        // the provider simulates granting an elevated permission during authentication
        var server = StartServer(
            networkPort: networkPort,
            testMethodFake: arg => RemotingSession.Current?.GetVariable<bool>("Elevated"),
            authenticationProvider: new VariableSettingAuthProvider(),
            onServerError: (s, ex) =>
            {
                Interlocked.Increment(ref serverErrorCount);
                lastServerError = ex;
            });

        try
        {
            using var client = CreateClient(
                networkPort,
                credentials: [new Credential { Name = "user", Value = "bozo" }]);

            await client.ConnectAsync();
            Assert.True(client.Identity.IsAuthenticated);

            // the variable set by the provider during auth is readable from service code after login
            var proxy = client.CreateProxy<ITestService>();
            Assert.True((bool)proxy.TestMethod("x"));

            var session = server.SessionRepository.Sessions.Single();
            Assert.True(session.HasVariable("Elevated"));
        }
        finally
        {
            if (lastServerError != null)
                throw new Exception($"Unexpected server error: {lastServerError}");

            Assert.Equal(0, serverErrorCount);

            server.Stop();
        }
    }

    [Fact]
    public async Task Session_variables_should_survive_parking_and_resume()
    {
        var serverErrorCount = 0;
        Exception lastServerError = null;
        int networkPort = Interlocked.Increment(ref _nextPort);

        var server = StartServer(
            networkPort: networkPort,
            testMethodFake: arg =>
            {
                var session = RemotingSession.Current;

                if ((string)arg == "set")
                {
                    session?.SetVariable("Flag", "abc");
                    return "ok";
                }

                return (object)session?.GetVariable<string>("Flag") ?? "<none>";
            },
            onServerError: (s, ex) =>
            {
                Interlocked.Increment(ref serverErrorCount);
                lastServerError = ex;
            });

        try
        {
            using var client = CreateClient(networkPort);
            await client.ConnectAsync();

            Guid? originalSessionId = client.SessionId;
            Assert.NotNull(originalSessionId);

            var proxy = client.CreateProxy<ITestService>();
            Assert.Equal("ok", proxy.TestMethod("set"));

            // abruptly kill the transport (no goodbye message, session must be parked on server)
            await HardKill(client);

            var parked = server.SessionRepository.Sessions.Single();
            Assert.True(parked.IsParked, "Session should be parked after an abrupt disconnect");
            Assert.True(parked.HasVariable("Flag"));

            // reconnect the same client instance - the resumed session must keep its variables
            await client.ConnectAsync();

            Assert.Equal(originalSessionId, client.SessionId);
            Assert.Single(server.SessionRepository.Sessions);

            var resumed = server.SessionRepository.Sessions.Single();
            Assert.False(resumed.IsParked);
            Assert.True(resumed.HasVariable("Flag"));
            Assert.Equal("abc", proxy.TestMethod("get"));
        }
        finally
        {
            if (lastServerError != null)
                throw new Exception($"Unexpected server error: {lastServerError}");

            Assert.Equal(0, serverErrorCount);

            server.Stop();
        }
    }

    [Fact]
    public async Task Concurrent_session_variable_access_should_be_safe()
    {
        var serverErrorCount = 0;
        Exception lastServerError = null;
        int networkPort = Interlocked.Increment(ref _nextPort);

        var server = StartServer(
            networkPort: networkPort,
            testMethodFake: arg =>
            {
                var session = RemotingSession.Current;

                var tasks = new List<Task>();

                for (var worker = 0; worker < 8; worker++)
                {
                    var localWorker = worker;

                    tasks.Add(Task.Run(() =>
                    {
                        for (var round = 0; round < 250; round++)
                        {
                            session.SetVariable($"key{localWorker % 7}", round);
                            session.GetVariable<object>($"key{(round + localWorker) % 7}");
                            session.HasVariable($"key{(round * 3 + localWorker) % 7}");
                        }
                    }));
                }

                Task.WhenAll(tasks).GetAwaiter().GetResult();
                return "done";
            },
            onServerError: (s, ex) =>
            {
                Interlocked.Increment(ref serverErrorCount);
                lastServerError = ex;
            });

        try
        {
            using var client = CreateClient(networkPort);
            await client.ConnectAsync();

            var proxy = client.CreateProxy<ITestService>();
            Assert.Equal("done", proxy.TestMethod("x"));

            // no exceptions were raised and the concurrently written keys are intact
            var session = server.SessionRepository.Sessions.Single();
            Assert.Contains("key0", session.Variables.Keys);
            Assert.IsType<int>(session.GetVariable<object>("key0"));
        }
        finally
        {
            if (lastServerError != null)
                throw new Exception($"Unexpected server error: {lastServerError}");

            Assert.Equal(0, serverErrorCount);

            server.Stop();
        }
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

    private static RemotingClient CreateClient(int serverPort, Credential[] credentials = null)
    {
        var config = new ClientConfig()
        {
            ConnectionTimeout = 0,
            MessageEncryption = true,
            KeySize = 1024,
            ServerHostName = "localhost",
            ServerPort = serverPort,
            KeepSessionAliveInterval = 0
        };

        if (credentials != null)
            config.Credentials = credentials;

        return new RemotingClient(config);
    }

    private static RemotingServer StartServer(
        int networkPort,
        Func<object, object> testMethodFake = null,
        IAuthenticationProvider authenticationProvider = null,
        EventHandler<Exception> onServerError = null)
    {
        var config = new ServerConfig()
        {
            IsDefault = false,
            UniqueServerInstanceName = $"SessionVariableTestServer_{networkPort}",
            MessageEncryption = true,
            KeySize = 1024,
            NetworkPort = networkPort,
            AuthenticationRequired = authenticationProvider != null,
            AuthenticationProvider = authenticationProvider,
            RegisterServicesAction = container =>
            {
                var testService = new TestService()
                {
                    TestMethodFake = testMethodFake ?? (arg => arg)
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

    /// <summary>
    /// Test authentication provider that grants an elevated permission variable during auth.
    /// </summary>
    private class VariableSettingAuthProvider : IAuthenticationProvider
    {
        public Task<AuthenticationResponseMessage> Authenticate(AuthenticationRequestMessage authRequestMessage)
        {
            RemotingSession.Current?.SetVariable("Elevated", true);

            return Task.FromResult(new AuthenticationResponseMessage
            {
                IsCompleted = true,
                IsAuthenticated = true,
                AuthenticatedIdentity = new RemotingIdentity
                {
                    Name = "bozo",
                    IsAuthenticated = true
                }
            });
        }
    }
}

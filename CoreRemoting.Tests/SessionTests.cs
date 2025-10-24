using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Channels;
using CoreRemoting.Tests.Tools;
using CoreRemoting.Toolbox;
using Xunit;

namespace CoreRemoting.Tests;

[Collection("CoreRemoting")]
[SuppressMessage("ReSharper", "AccessToDisposedClosure")]
[SuppressMessage("ReSharper", "CoVariantArrayConversion")]
public class SessionTests : IClassFixture<ServerFixture>
{
    private readonly ServerFixture _serverFixture;

    public SessionTests(ServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        _serverFixture.Start(ServerChannel);
    }

    protected virtual IClientChannel ClientChannel => null;
    protected virtual IServerChannel ServerChannel => null;

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task Client_Connect_should_create_new_session_AND_Disconnect_should_close_session()
    {
        using var ctx = ValidationSyncContext.Install();

        var clientStarted1 = new TaskCompletionSource();
        var clientStarted2 = new TaskCompletionSource();
        var clientStopSignal = new TaskCompletionSource();

        async Task ClientTask(TaskCompletionSource connected)
        {
            var client = new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 0,
                MessageEncryption = false,
                ServerPort = _serverFixture.Server.Config.NetworkPort,
                Channel = ClientChannel
            });

            Assert.False(client.HasSession);
            client.Connect();

            connected.TrySetResult();
            Assert.True(client.HasSession);

            await clientStopSignal.Task.ConfigureAwait(false);
            client.Dispose();
        }

        // There should be no sessions, before both clients connected
        Assert.Empty(_serverFixture.Server.SessionRepository.Sessions);

        // Start two clients to create two sessions
        var client1 = ClientTask(clientStarted1);
        var client2 = ClientTask(clientStarted2);

        // Wait for connection of both clients
        await Task.WhenAll(clientStarted1.Task, clientStarted2.Task).Timeout(5).ConfigureAwait(false);

        Assert.Equal(2, _serverFixture.Server.SessionRepository.Sessions.Count());

        clientStopSignal.TrySetResult();

        await Task.WhenAll(client1, client2, Task.Delay(100)).Timeout(5).ConfigureAwait(false);

        // There should be no sessions left, after both clients disconnected
        Assert.Empty(_serverFixture.Server.SessionRepository.Sessions);
    }

    [Fact]
    public void Client_Connect_should_throw_exception_on_invalid_auth_credentials()
    {
        using var ctx = ValidationSyncContext.Install();

        var serverConfig =
            new ServerConfig()
            {
                UniqueServerInstanceName = "AuthServer",
                IsDefault = false,
                MessageEncryption = false,
                NetworkPort = 9095,
                AuthenticationRequired = true,
                AuthenticationProvider = new FakeAuthProvider()
                {
                    AuthenticateFake = credentials => credentials[1].Value == "secret"
                },
                Channel = ServerChannel
            };
        
        var server = new RemotingServer(serverConfig);
        server.Start();

        try
        {
            var clientAction = new Action<string, bool>((password, shouldThrow) =>
            {
                using var client = 
                    new RemotingClient(new ClientConfig()
                    {
                        ConnectionTimeout = 0,
                        ServerPort = server.Config.NetworkPort,
                        MessageEncryption = false,
                        Credentials =
                        [
                            new() { Name = "User", Value = "tester" },
                            new() { Name = "Password", Value = password }
                        ],
                        Channel = ClientChannel
                    });
            
                if (shouldThrow)
                    Assert.Throws<SecurityException>(() => client.Connect());
                else
                    client.Connect();
            });

            var clientThread1 = new Thread(() => clientAction("wrong", true));
            clientThread1.Start();
            clientThread1.Join();
        
            var clientThread2 = new Thread(() => clientAction("secret", false));
            clientThread2.Start();
            clientThread2.Join();

            Assert.Equal(0, _serverFixture.ServerErrorCount);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public void RemotingSession_Dispose_should_disconnect_client()
    {
        _serverFixture.TestService.TestMethodFake = _ =>
        {
            RemotingSession.Current.Close();
            return null;
        };

        using var ctx = ValidationSyncContext.Install();

        var client =
            new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 0,
                MessageEncryption = false,
                SendTimeout = 0,
                ServerPort = _serverFixture.Server.Config.NetworkPort,
                Channel = ClientChannel
            });

        client.Connect();
        var proxy = client.CreateProxy<ITestService>();

        proxy.TestMethod(null);
        client.Dispose();
    }

    [Fact]
    public void RemotingSession_should_be_accessible_to_the_component_constructor()
    {
        using var ctx = ValidationSyncContext.Install();

        using var client = new RemotingClient(new ClientConfig()
        {
            ConnectionTimeout = 0,
            InvocationTimeout = 0,
            SendTimeout = 0,
            MessageEncryption = false,
            ServerPort = _serverFixture.Server.Config.NetworkPort,
            Channel = ClientChannel
        });

        client.Connect();

        // RemotingSession.Current should be accessible to the component constructor
        var proxy = client.CreateProxy<ISessionAwareService>();

        // RemotingSession should be the same as in the constructor
        Assert.True(proxy.HasSameSessionInstance);
    }

    [Fact]
    public virtual async Task CloseSession_method_should_close_session_gracefully_issue55_and156()
    {
        using var client = new RemotingClient(new ClientConfig()
        {
            ConnectionTimeout = 0,
            InvocationTimeout = 0,
            SendTimeout = 0,
            MessageEncryption = false,
            ServerPort = _serverFixture.Server.Config.NetworkPort,
            Channel = ClientChannel
        });

        var disconnected = new TaskCompletionSource<bool>();
        client.AfterDisconnect += () => disconnected.TrySetResult(true);
        client.Connect();

        // RemotingSession.Current should be accessible to the component constructor
        var proxy = client.CreateProxy<ISessionAwareService>();

        // Wait(1s) should finish after CloseSession(0.5s)
        var proxy_Wait = proxy.Wait(1);

        // CloseSession shouldn't throw exceptions
        await proxy.CloseSession(0.5);

        // proxy.Wait was started before CloseSession and shouldn't throw either
        await proxy_Wait;

        // Disconnection event should occur
        await disconnected.Task.Timeout(2);
    }

    [Fact]
    public virtual async Task Client_shouldnt_time_out_when_Disconnect_is_called_more_than_once_issue_192()
    {
        using var client = new RemotingClient(new ClientConfig()
        {
            ConnectionTimeout = 0,
            InvocationTimeout = 0,
            SendTimeout = 0,
            MessageEncryption = false,
            ServerPort = _serverFixture.Server.Config.NetworkPort,
            Channel = ClientChannel,
            WaitTimeForCurrentlyProcessedMessagesOnDispose = 10000 // 20 seconds for really long wait
        });

        client.Connect();

        var proxy = client.CreateProxy<ISessionAwareService>();
        await proxy.CloseSession(0.5); // server sends close_session
        await Task.Delay(TimeSpan.FromSeconds(1)); // client gets close_session and disconnects
        await Task.Run(client.Dispose).Timeout(2); // client calls Dispose and disconnects
    }

    [Fact]
    public virtual async Task Client_shouldnt_time_out_when_dispose_is_called_after_remote_disconnect()
    {
        _serverFixture.TestService.TestMethodFake = _ =>
        {
            RemotingSession.Current.Close();
            return null;
        };
        
        using var client = new RemotingClient(new ClientConfig
        {
            ConnectionTimeout = 0,
            InvocationTimeout = 0,
            SendTimeout = 0,
            MessageEncryption = false,
            ServerPort = _serverFixture.Server.Config.NetworkPort,
            Channel = ClientChannel,
            WaitTimeForGoodbyeOnDisconnect = 100 //We will set such a timeout so that the client is guaranteed not to wait more than 3 seconds.
        });

        client.Connect();
        
        var proxy = client.CreateProxy<ITestService>();

        proxy.TestMethod(null);// server sends close_session
        await Task.Run(client.Dispose).Timeout(3); // client calls Dispose and disconnects
    }
}


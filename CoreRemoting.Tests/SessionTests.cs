using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Authentication;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests
{
    [Collection("CoreRemoting")]
    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    [SuppressMessage("ReSharper", "CoVariantArrayConversion")]
    public class SessionTests : IClassFixture<ServerFixture>
    {
        private ServerFixture _serverFixture;
        private readonly ITestOutputHelper _testOutputHelper;
        
        public SessionTests(ServerFixture serverFixture, ITestOutputHelper testOutputHelper)
        {
            _serverFixture = serverFixture;
            _testOutputHelper = testOutputHelper;
        }
        
        [Fact]
        public void Client_Connect_should_create_new_session_AND_Disconnect_should_close_session()
        {
            var connectedWaitHandles = new[]
            {
                new AutoResetEvent(false),
                new AutoResetEvent(false)
            };

            var quitWaitHandle = new ManualResetEventSlim();

            var clientAction = new Action<int>(index =>
            {
                var client =
                    new RemotingClient(new ClientConfig()
                    {
                        ConnectionTimeout = 0,
                        MessageEncryption = false,
                        ServerPort = _serverFixture.Server.Config.NetworkPort
                    });

                Assert.False(client.HasSession);
                client.Connect();

                connectedWaitHandles[index].Set();

                Assert.True(client.HasSession);

                quitWaitHandle.Wait();

                client.Dispose();
            });
            
            var clientThread1 = new Thread(() => clientAction(0));
            var clientThread2 = new Thread(() => clientAction(1));

            Assert.Empty(_serverFixture.Server.SessionRepository.Sessions);

            // Start two clients to create two sessions
            clientThread1.Start();
            clientThread2.Start();

            // Wait for connection of both clients
            WaitHandle.WaitAll(connectedWaitHandles);

            Assert.Equal(2, _serverFixture.Server.SessionRepository.Sessions.Count());

            quitWaitHandle.Set();

            clientThread1.Join();
            clientThread2.Join();

            // There should be no sessions left, after both clients disconnedted
            Assert.Empty(_serverFixture.Server.SessionRepository.Sessions);
        }

        [Fact]
        public void Client_Connect_should_throw_exception_on_invalid_auth_credentials()
        {
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
                    }
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
                            Credentials = new []
                            {
                                new Credential() { Name = "User", Value = "tester" },
                                new Credential() {Name = "Password", Value = password }
                            }
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
            _serverFixture.TestService.TestMethodFake = arg =>
            {
                RemotingSession.Current.Close();
                return null;
            };

            var client =
                new RemotingClient(new ClientConfig()
                {
                    ConnectionTimeout = 0,
                    MessageEncryption = false,
                    SendTimeout = 0,
                    ServerPort = _serverFixture.Server.Config.NetworkPort
                });

            var waitForDisconnect = new ManualResetEventSlim(initialState: false);

            client.AfterDisconnect += () =>
            {
                waitForDisconnect.Set();
            };
            
            client.Connect();
            var proxy = client.CreateProxy<ITestService>();

            proxy.TestMethod(null);

            waitForDisconnect.Wait();
            Assert.False(client.IsConnected);
            
            client.Dispose();
        }
    }
}
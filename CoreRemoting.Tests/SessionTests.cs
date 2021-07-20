using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security;
using System.Threading;
using CoreRemoting.Authentication;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests
{
    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    [SuppressMessage("ReSharper", "CoVariantArrayConversion")]
    public class SessionTests
    {
        [Fact]
        public void Client_Connect_should_create_new_session_AND_Disconnect_should_close_session()
        {
            var testService =
                new TestService();

            var serverConfig =
                new ServerConfig()
                {
                    NetworkPort = 9091,
                    RegisterServicesAction = container =>
                        container.RegisterService<ITestService>(
                            factoryDelegate: () => testService,
                            lifetime: ServiceLifetime.Singleton)
                };

            using var server = new RemotingServer(serverConfig);
            server.Start();

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
                        ServerPort = 9091
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

            Assert.Empty(server.SessionRepository.Sessions);

            // Start two clients to create two sessions
            clientThread1.Start();
            clientThread2.Start();

            // Wait for connection of both clients
            WaitHandle.WaitAll(connectedWaitHandles);

            Assert.Equal(2, server.SessionRepository.Sessions.Count());

            quitWaitHandle.Set();

            clientThread1.Join();
            clientThread2.Join();

            // There should be no sessions left, after both clients disconnedted
            Assert.Empty(server.SessionRepository.Sessions);
        }

        [Fact]
        public void Client_Connect_should_throw_exception_on_invalid_auth_credentials()
        {
            var testService = new TestService();
            var authProvider =
                new FakeAuthProvider()
                {
                    AuthenticateFake = credentials => credentials[1].Value == "secret"
                };

            var serverConfig =
                new ServerConfig()
                {
                    AuthenticationProvider = authProvider,
                    AuthenticationRequired = true,
                    NetworkPort = 9092,
                    RegisterServicesAction = container =>
                        container.RegisterService<ITestService>(
                            factoryDelegate: () => testService,
                            lifetime: ServiceLifetime.Singleton)
                };

            int serverErrorCount = 0;
            
            using var server = new RemotingServer(serverConfig);
            server.Error += (_, _) => serverErrorCount++;
            server.Start();

            var clientAction = new Action<string, bool>((password, shouldThrow) =>
            {
                using var client = 
                    new RemotingClient(new ClientConfig()
                    {
                        ConnectionTimeout = 0,
                        ServerPort = 9092,
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

            Assert.Equal(0, serverErrorCount);
        }
    }
}
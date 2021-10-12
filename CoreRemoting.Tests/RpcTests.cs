using System;
using System.Threading;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.ExternalTypes;
using CoreRemoting.Tests.Tools;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests
{
    public class RpcTests
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public RpcTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Fact]
        public void Call_on_Proxy_should_be_invoked_on_remote_service()
        {
            bool remoteServiceCalled = false;

            var testService =
                new TestService()
                {
                    TestMethodFake = arg =>
                    {
                        remoteServiceCalled = true;
                        return arg;
                    }
                };
            
            var serverConfig =
                new ServerConfig()
                {
                    NetworkPort = 9094,
                    RegisterServicesAction = container =>
                        container.RegisterService<ITestService>(
                            factoryDelegate: () => testService,
                            lifetime: ServiceLifetime.Singleton)
                };

            int serverErrorCount = 0;
            
            using var server = new RemotingServer(serverConfig);
            server.Error += (_, _) => serverErrorCount++;
            server.Start();

            void ClientAction()
            {
                try
                {
                    using var client = new RemotingClient(new ClientConfig()
                    {
                        ConnectionTimeout = 0, 
                        ServerPort = 9094
                    });

                    client.Connect();

                    var proxy = client.CreateProxy<ITestService>();
                    var result = proxy.TestMethod("test");

                    Assert.Equal("test", result);
                }
                catch (Exception e)
                {
                    _testOutputHelper.WriteLine(e.ToString());
                    throw;
                }
            }

            var clientThread = new Thread(ClientAction);
            clientThread.Start();
            clientThread.Join();
            
            Assert.True(remoteServiceCalled);
            Assert.Equal(0, serverErrorCount);
        }
        
        [Fact]
        public void Call_on_Proxy_should_be_invoked_on_remote_service_without_MessageEncryption()
        {
            bool remoteServiceCalled = false;

            var testService =
                new TestService()
                {
                    TestMethodFake = arg =>
                    {
                        remoteServiceCalled = true;
                        return arg;
                    }
                };
            
            var serverConfig =
                new ServerConfig()
                {
                    MessageEncryption = false,
                    NetworkPort = 9094,
                    RegisterServicesAction = container =>
                        container.RegisterService<ITestService>(
                            factoryDelegate: () => testService,
                            lifetime: ServiceLifetime.Singleton)
                };

            int serverErrorCount = 0;
            
            using var server = new RemotingServer(serverConfig);
            server.Error += (_, _) => serverErrorCount++;
            server.Start();

            void ClientAction()
            {
                try
                {
                    using var client = new RemotingClient(new ClientConfig()
                    {
                        ConnectionTimeout = 0, 
                        ServerPort = 9094,
                        MessageEncryption = false
                    });

                    client.Connect();

                    var proxy = client.CreateProxy<ITestService>();
                    var result = proxy.TestMethod("test");

                    Assert.Equal("test", result);
                }
                catch (Exception e)
                {
                    _testOutputHelper.WriteLine(e.ToString());
                    throw;
                }
            }

            var clientThread = new Thread(ClientAction);
            clientThread.Start();
            clientThread.Join();
            
            Assert.True(remoteServiceCalled);
            Assert.Equal(0, serverErrorCount);
        }

        [Fact]
        public void Delegate_invoked_on_server_should_callback_client()
        {
            string argumentFromServer = null;

            var testService = new TestService();
            
            var serverConfig =
                new ServerConfig()
                {
                    NetworkPort = 9095,
                    RegisterServicesAction = container =>
                        container.RegisterService<ITestService>(
                            factoryDelegate: () => testService,
                            lifetime: ServiceLifetime.Singleton)
                };

            int serverErrorCount = 0;
            
            using var server = new RemotingServer(serverConfig);
            server.Error += (_, _) => serverErrorCount++;
            server.Start();

            void ClientAction()
            {
                try
                {
                    using var client = new RemotingClient(
                        new ClientConfig()
                        {
                            ConnectionTimeout = 0, 
                            ServerPort = 9095,
                        });

                    client.Connect();

                    var proxy = client.CreateProxy<ITestService>();
                    proxy.TestMethodWithDelegateArg(arg => argumentFromServer = arg);
                }
                catch (Exception e)
                {
                    _testOutputHelper.WriteLine(e.ToString());
                    throw;
                }
            }

            var clientThread = new Thread(ClientAction);
            clientThread.Start();
            clientThread.Join();
                
            Assert.Equal("test", argumentFromServer);
            Assert.Equal(0, serverErrorCount);
        }
        
        [Fact]
        public void Events_should_work_remotly()
        {
            var testService = new TestService();
            
            var serverConfig =
                new ServerConfig()
                {
                    NetworkPort = 9096,
                    RegisterServicesAction = container =>
                        container.RegisterService<ITestService>(
                            factoryDelegate: () => testService,
                            lifetime: ServiceLifetime.Singleton)
                };

            int serverErrorCount = 0;
            bool serviceEventCalled = false;
            
            using var server = new RemotingServer(serverConfig);
            server.Error += (_, _) => serverErrorCount++;
            server.Start();
            
            using var client = new RemotingClient(
                new ClientConfig()
                {
                    ConnectionTimeout = 0, 
                    ServerPort = 9096
                });

            client.Connect();

            var proxy = client.CreateProxy<ITestService>();
            
            proxy.ServiceEvent += () => 
                serviceEventCalled = true;
            
            proxy.FireServiceEvent();

            Assert.True(serviceEventCalled);
            Assert.Equal(0, serverErrorCount);
        }
        
        [Fact]
        public void External_types_should_work_as_remote_service_parameters()
        {
            bool remoteServiceCalled = false;
            DataClass parameterValue = null;

            var testService =
                new TestService()
                {
                    TestExternalTypeParameterFake = arg =>
                    {
                        remoteServiceCalled = true;
                        parameterValue = arg;
                    }
                };
            
            var serverConfig =
                new ServerConfig()
                {
                    NetworkPort = 9097,
                    RegisterServicesAction = container =>
                        container.RegisterService<ITestService>(
                            factoryDelegate: () => testService,
                            lifetime: ServiceLifetime.Singleton)
                };

            int serverErrorCount = 0;
            
            using var server = new RemotingServer(serverConfig);
            server.Error += (_, _) => serverErrorCount++;
            server.Start();

            void ClientAction()
            {
                try
                {
                    using var client = new RemotingClient(new ClientConfig()
                    {
                        ConnectionTimeout = 0, 
                        ServerPort = 9097
                    });

                    client.Connect();

                    var proxy = client.CreateProxy<ITestService>();
                    proxy.TestExternalTypeParameter(new DataClass() {Value = 42});

                    Assert.Equal(42, parameterValue.Value);
                }
                catch (Exception e)
                {
                    _testOutputHelper.WriteLine(e.ToString());
                    throw;
                }
            }

            var clientThread = new Thread(ClientAction);
            clientThread.Start();
            clientThread.Join();
            
            Assert.True(remoteServiceCalled);
            Assert.Equal(0, serverErrorCount);
        }
        
        #region Service with generic method
        
        public interface IGenericEchoService
        {
            T Echo<T>(T value);
        }

        public class GenericEchoService : IGenericEchoService
        {
            public T Echo<T>(T value)
            {
                return value;
            }
        }

        #endregion
        
        [Fact]
        public void Generic_methods_should_be_called_correctly()
        {
            var serverConfig =
                new ServerConfig()
                {
                    NetworkPort = 9197,
                    RegisterServicesAction = container =>
                        container.RegisterService<IGenericEchoService, GenericEchoService>(
                            lifetime: ServiceLifetime.Singleton)
                };

            using var server = new RemotingServer(serverConfig);
            server.Start();

            using var client = new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 0, 
                ServerPort = 9197
            });

            client.Connect();
            var proxy = client.CreateProxy<IGenericEchoService>();

            var result = proxy.Echo("Yay");
            
            Assert.Equal("Yay", result);
        }
    }
}
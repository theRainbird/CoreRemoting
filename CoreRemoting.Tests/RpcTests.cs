using System;
using System.Diagnostics;
using System.Threading;
using CoreRemoting.Tests.ExternalTypes;
using CoreRemoting.Tests.Tools;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests
{
    [Collection("CoreRemoting")]
    public class RpcTests : IClassFixture<ServerFixture>
    {
        private readonly ServerFixture _serverFixture;
        private readonly ITestOutputHelper _testOutputHelper;
        private bool _remoteServiceCalled;
        
        public RpcTests(ServerFixture serverFixture, ITestOutputHelper testOutputHelper)
        {
            _serverFixture = serverFixture;
            _testOutputHelper = testOutputHelper;
        
            _serverFixture.TestService.TestMethodFake = arg =>
            {
                _remoteServiceCalled = true;
                return arg;
            };
        }

        [Fact]
        public void Call_on_Proxy_should_be_invoked_on_remote_service()
        {
            void ClientAction()
            {
                try
                {
                    var stopWatch = new Stopwatch();
                    stopWatch.Start();
                    
                    using var client = new RemotingClient(new ClientConfig()
                    {
                        ConnectionTimeout = 0, 
                        MessageEncryption = false,
                        ServerPort = _serverFixture.Server.Config.NetworkPort
                    });

                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Creating client took {stopWatch.ElapsedMilliseconds} ms");
                    stopWatch.Reset();
                    stopWatch.Start();
                    
                    client.Connect();

                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Establishing connection took {stopWatch.ElapsedMilliseconds} ms");
                    stopWatch.Reset();
                    stopWatch.Start();
                    
                    var proxy = client.CreateProxy<ITestService>();
                    
                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Creating proxy took {stopWatch.ElapsedMilliseconds} ms");
                    stopWatch.Reset();
                    stopWatch.Start();
                    
                    var result = proxy.TestMethod("test");

                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Remote method invocation took {stopWatch.ElapsedMilliseconds} ms");
                    stopWatch.Reset();
                    stopWatch.Start();
                    
                    var result2 = proxy.TestMethod("test");

                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Second remote method invocation took {stopWatch.ElapsedMilliseconds} ms");
                    
                    Assert.Equal("test", result);
                    Assert.Equal("test", result2);
                    
                    proxy.MethodWithOutParameter(out int methodCallCount);
                    
                    Assert.Equal(1, methodCallCount);
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
            
            Assert.True(_remoteServiceCalled);
            Assert.Equal(0,  _serverFixture.ServerErrorCount);
        }
        
        [Fact]
        public void Call_on_Proxy_should_be_invoked_on_remote_service_with_MessageEncryption()
        {
            _serverFixture.Server.Config.MessageEncryption = true;
            
            void ClientAction()
            {
                try
                {
                    var stopWatch = new Stopwatch();
                    stopWatch.Start();
                    
                    using var client = new RemotingClient(new ClientConfig()
                    {
                        ConnectionTimeout = 0, 
                        ServerPort = _serverFixture.Server.Config.NetworkPort,
                        MessageEncryption = true
                    });

                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Creating client took {stopWatch.ElapsedMilliseconds} ms");
                    stopWatch.Reset();
                    stopWatch.Start();
                    
                    client.Connect();

                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Establishing connection took {stopWatch.ElapsedMilliseconds} ms");
                    stopWatch.Reset();
                    stopWatch.Start();
                    
                    var proxy = client.CreateProxy<ITestService>();
                    
                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Creating proxy took {stopWatch.ElapsedMilliseconds} ms");
                    stopWatch.Reset();
                    stopWatch.Start();
                    
                    var result = proxy.TestMethod("test");

                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Remote method invocation took {stopWatch.ElapsedMilliseconds} ms");
                    stopWatch.Reset();
                    stopWatch.Start();
                    
                    var result2 = proxy.TestMethod("test");

                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Second remote method invocation took {stopWatch.ElapsedMilliseconds} ms");
                    
                    Assert.Equal("test", result);
                    Assert.Equal("test", result2);
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
            
            _serverFixture.Server.Config.MessageEncryption = true;
            
            Assert.True(_remoteServiceCalled);
            Assert.Equal(0, _serverFixture.ServerErrorCount);
        }

        [Fact]
        public void Delegate_invoked_on_server_should_callback_client()
        {
            string argumentFromServer = null;

            void ClientAction()
            {
                try
                {
                    using var client = new RemotingClient(
                        new ClientConfig()
                        {
                            ConnectionTimeout = 0, 
                            MessageEncryption = false,
                            ServerPort = _serverFixture.Server.Config.NetworkPort,
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
            Assert.Equal(0, _serverFixture.ServerErrorCount);
        }
        
        [Fact]
        public void Events_should_work_remotly()
        {
            bool serviceEventCalled = false;
            bool customDelegateEventCalled = false;
         
            using var client = new RemotingClient(
                new ClientConfig()
                {
                    ConnectionTimeout = 0, 
                    SendTimeout = 0,
                    MessageEncryption = false,
                    ServerPort = _serverFixture.Server.Config.NetworkPort,
                });

            client.Connect();

            var proxy = client.CreateProxy<ITestService>();

            var serviceEventResetEvent = new ManualResetEventSlim(initialState: false);
            var customDelegateEventResetEvent = new ManualResetEventSlim(initialState: false);

            proxy.ServiceEvent += () =>
            {
                serviceEventCalled = true;
                serviceEventResetEvent.Set();
            };

            proxy.CustomDelegateEvent += _ =>
            {
                customDelegateEventCalled = true;
                customDelegateEventResetEvent.Set();
            };
             
            proxy.FireServiceEvent();
            proxy.FireCustomDelegateEvent();

            serviceEventResetEvent.Wait(1000);
            customDelegateEventResetEvent.Wait(1000);
            
            Assert.True(serviceEventCalled);
            Assert.True(customDelegateEventCalled);
            Assert.Equal(0, _serverFixture.ServerErrorCount);
        }
        
        [Fact]
        public void External_types_should_work_as_remote_service_parameters()
        {
            DataClass parameterValue = null;

            _serverFixture.TestService.TestExternalTypeParameterFake = arg =>
            {
                _remoteServiceCalled = true;
                parameterValue = arg;
            };

            void ClientAction()
            {
                try
                {
                    using var client = new RemotingClient(new ClientConfig()
                    {
                        ConnectionTimeout = 0, 
                        MessageEncryption = false,
                        ServerPort = _serverFixture.Server.Config.NetworkPort,
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
            
            Assert.True(_remoteServiceCalled);
            Assert.Equal(0, _serverFixture.ServerErrorCount);
        }
        
        [Fact]
        public void Generic_methods_should_be_called_correctly()
        {
            using var client = new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 0, 
                MessageEncryption = false,
                ServerPort = _serverFixture.Server.Config.NetworkPort,
            });

            client.Connect();
            var proxy = client.CreateProxy<IGenericEchoService>();

            var result = proxy.Echo("Yay");
            
            Assert.Equal("Yay", result);
        }
        
        [Fact]
        public void Inherited_methods_should_be_called_correctly()
        {
            using var client = new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 0, 
                MessageEncryption = false,
                ServerPort = _serverFixture.Server.Config.NetworkPort,
            });

            client.Connect();
            var proxy = client.CreateProxy<ITestService>();

            var result = proxy.BaseMethod();
            
            Assert.True(result);
        }
        
        [Fact]
        public void Enum_arguments_should_be_passed_correctly()
        {
            using var client = new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 0, 
                MessageEncryption = false,
                ServerPort = _serverFixture.Server.Config.NetworkPort
            });

            client.Connect();
            var proxy = client.CreateProxy<IEnumTestService>();

            var resultFirst = proxy.Echo(TestEnum.First);
            var resultSecond = proxy.Echo(TestEnum.Second);
            
            Assert.Equal(TestEnum.First, resultFirst);
            Assert.Equal(TestEnum.Second, resultSecond);
        }
    }
}
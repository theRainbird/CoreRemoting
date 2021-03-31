using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.ClassicRemotingApi;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests
{
    public class StressTests
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public StressTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }
        
        [Fact]
        public void Executing101RpcCalls_should_not_bloat_memory()
        {
            int executionCount = 0;

            var testService =
                new TestService()
                {
                    TestMethodFake = arg =>
                    {
                        Interlocked.Increment(ref executionCount);
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

            using var server = new RemotingServer(serverConfig);
            server.Start();

            var onlyOneCall = true;
            
            void ClientAction()
            {
                using var client = new RemotingClient(new ClientConfig()
                {
                    ConnectionTimeout = 0, 
                    ServerPort = 9094
                });

                client.Connect();
                var proxy = client.CreateProxy<ITestService>();

                // ReSharper disable once AccessToModifiedClosure
                if (onlyOneCall)
                    proxy.TestMethod("test");
                else
                {
                    Parallel.For(0, 100, i =>
                    {
                        var result = proxy.TestMethod("test");
                    });    
                }
                
                client.Disconnect();
            }

            var firstClientThread = new Thread(ClientAction);
            firstClientThread.Start();
            firstClientThread.Join();

            onlyOneCall = false;

            long consumedMemoryAfterOneCall = Process.GetCurrentProcess().PrivateMemorySize64;
            
            _testOutputHelper.WriteLine("{0} = {1} MB", nameof(consumedMemoryAfterOneCall), consumedMemoryAfterOneCall / 1024 / 1024);
            
            var secondClientThread = new Thread(ClientAction);
            secondClientThread.Start();
            secondClientThread.Join();
            
            long consumedMemoryAfter101Calls = Process.GetCurrentProcess().PrivateMemorySize64;
            
            _testOutputHelper.WriteLine("{0} = {1} MB", nameof(consumedMemoryAfter101Calls), consumedMemoryAfter101Calls / 1024 / 1024);
            _testOutputHelper.WriteLine("Difference: {0} MB", (consumedMemoryAfter101Calls - consumedMemoryAfterOneCall) / 1024 / 1024);
            
            Assert.Equal(101, executionCount);
            Assert.True((consumedMemoryAfter101Calls - consumedMemoryAfterOneCall) < (128 * 1024 * 1024)); // 128 MB
        }
    }
}
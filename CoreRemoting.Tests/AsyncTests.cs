using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests
{
    public class AsyncTests : IClassFixture<ServerFixture>
    {
        private ServerFixture _serverFixture;

        public AsyncTests(ServerFixture serverFixture)
        {
            _serverFixture = serverFixture;
        }
        
        [Fact]
        public async void AsyncMethods_should_work()
        {
            using var client = new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 0,
                InvocationTimeout = 0,
                ServerPort = _serverFixture.Server.Config.NetworkPort
            });

            client.Connect();
            var proxy = client.CreateProxy<IAsyncService>();

            var base64String = await proxy.ConvertToBase64Async("Yay");

            Assert.Equal("WWF5", base64String);
        }

        /// <summary>
        /// Awaiting for ordinary non-generic task method should not hangs. 
        /// </summary>
        [Fact(Timeout = 15000)]
        public async void AwaitingNonGenericTask_should_not_hang_forever()
        {
            using var client = new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 0,
                InvocationTimeout = 0,
                ServerPort = _serverFixture.Server.Config.NetworkPort
            });

            client.Connect();
            var proxy = client.CreateProxy<IAsyncService>();

            await proxy.NonGenericTask();
        }
    }
}
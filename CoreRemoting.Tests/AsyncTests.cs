using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CoreRemoting.DependencyInjection;
using Xunit;

namespace CoreRemoting.Tests
{
    public class AsyncTests
    {
        #region Service with async method

        public interface IAsyncService
        {
            Task<string> ConvertToBase64Async(string text);

            Task NonGenericTask();
        }

        public class AsyncService : IAsyncService
        {
            public async Task<string> ConvertToBase64Async(string text)
            {
                var convertFunc = new Func<string>(() =>
                {
                    var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
                    return Convert.ToBase64String(stream.ToArray());
                });

                var base64String = await Task.Run(convertFunc);

                return base64String;
            }

            public Task NonGenericTask()
            {
                return Task.CompletedTask;
            }
        }

        #endregion

        [Fact]
        public async void AsyncMethods_should_work()
        {
            var serverConfig =
                new ServerConfig()
                {
                    NetworkPort = 9196,
                    RegisterServicesAction = container =>
                        container.RegisterService<IAsyncService, AsyncService>(
                            lifetime: ServiceLifetime.Singleton)
                };

            using var server = new RemotingServer(serverConfig);
            server.Start();

            using var client = new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 0,
                InvocationTimeout = 0,
                ServerPort = 9196
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
            var port = 9197;
            
            var serverConfig =
                new ServerConfig()
                {
                    NetworkPort = port,
                    RegisterServicesAction = container =>
                        container.RegisterService<IAsyncService, AsyncService>(
                            lifetime: ServiceLifetime.Singleton)
                };

            using var server = new RemotingServer(serverConfig);
            server.Start();

            using var client = new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 0,
                InvocationTimeout = 0,
                ServerPort = port
            });

            client.Connect();
            var proxy = client.CreateProxy<IAsyncService>();

            await proxy.NonGenericTask();
        }
    }
}
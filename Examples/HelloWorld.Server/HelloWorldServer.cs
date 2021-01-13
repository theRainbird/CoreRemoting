using System;
using CoreRemoting;
using CoreRemoting.DependencyInjection;
using HelloWorld.Shared;

namespace HelloWorld.Server
{
    public class SayHelloService : ISayHelloService
    {
        public string SayHello(string name)
        {
            return $"Hello {name}";
        }
    }

    public static class HelloWorldServer
    {
        static void Main(string[] args)
        {
            using var server = new RemotingServer(new ServerConfig()
            {
                HostName = "localhost",
                NetworkPort = 9090,
                RegisterServicesAction = container =>
                {
                    container.RegisterService<ISayHelloService, SayHelloService>(ServiceLifetime.Singleton);
                }
            });
            
            server.Start();
            
            Console.WriteLine("Server is running.");
            Console.ReadLine();
        }
    }
}
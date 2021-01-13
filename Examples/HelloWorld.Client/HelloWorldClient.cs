using System;
using CoreRemoting;
using HelloWorld.Shared;

namespace HelloWorld.Client
{
    public static class HelloWorldClient
    {
        static void Main(string[] args)
        {
            using var client = new RemotingClient(new ClientConfig()
            {
                ServerHostName = "localhost",
                ServerPort = 9090
            });
            
            client.Connect();

            var proxy = client.CreateProxy<ISayHelloService>();
            
            Console.WriteLine("What's your name?");
            var name = Console.ReadLine();

            var result = proxy.SayHello(name);
            Console.WriteLine(result);
        }
    }
}
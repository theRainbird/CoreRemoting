# CoreRemoting
RPC library (.NET Standard 2.0) with classic .NET Remoting flavour

### What is it for?
- To help migrate applications that use .NET Remoting to .NET Core / .NET 5.
- To provide easy-to-use RPC functionality
- To support events and delegates in a distributed application
- To run on Linux, Windows and Mac

### What is it NOT for?
- To create REST-APIs for Javascript clients
- To create SOAP Webservices
- To be create server applications that needs to run on several cluster nodes

## Hello world example
### Shared contracts
```csharp
namespace HelloWorld.Shared
{
    public interface ISayHelloService
    {
        string SayHello(string name);
    }
}
```
### Server
```csharp
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
```

### Client
```csharp
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
```

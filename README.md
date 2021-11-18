# CoreRemoting
RPC library (.NET Standard 2.0) with classic .NET Remoting flavour

NuGet package: https://www.nuget.org/packages/CoreRemoting/<br>
Documentation: https://github.com/theRainbird/CoreRemoting/wiki

### What is it for?
- To help migrate applications that use .NET Remoting to .NET Core / .NET 5.
- To provide easy-to-use RPC functionality
- To support events and delegates in a distributed application
- To run on Linux, Windows and Mac

### What is it NOT for?
- To create REST-APIs for Javascript clients
- To create SOAP Webservices
- To use with other platforms than .NET
- To create server applications that needs to run on several cluster nodes

## Facts & features
- Creates proxy objects for remote services at runtime (uses Castle.DynamicProxy under the hood)
- Services can have `SingleCall` or `Singeton` lifetime
- Uses websockets for TCP duplex network communication by default (based on webshocket-sharp)
- Custom transport channels can be plugged in (Just implement `IServerChannel` and `IClientChannel`)
- Used Bson serialization by default (via Json.NET)
- Also supports classic BinaryFormatter for best possible DataSet / DataTable support
- BinaryFormatter is hardened against known deserialization attack patterns
- Custom serializers can be plugged in (Just implement `ISerializerAdapter`)
- Support for custom authentication (Just implement `IAuthenticationProvider`)
- Pluggable authentication provider to authenticate Linux user on server with PAM is available
- Pluggable authentication provider to authenticate Windows user on server is available
- Message encryption with RSA key exchange and AES (No SSL, no X509 certificates needed, works also on Linux)
- Supports .NET Remoting style `CallContext` (also on .NET Core / .NET 5) to implicitly transfer objects on RPC calls / threads
- Supports Microsoft Dependency Injection (Just call `AddCoreRemotingServer` or `AddCoreRemotingClient` on your `IServiceCollection`)
- Supports also Castle Windsor Container to provide Dependecy Injection
- Built-in session management
- Automatic sweeping of inactive sessions
- Keep session alive feature
- Can be used in Blazor Server projects to communicate to a central application server
- Supports Linq Expression parameters

## Hello world example 
Let's create a simple multi user chat server as hello world application.

### Shared contract
To be able to call a remote service, the client needs to know an interface implemented by the service.
This interfaces should be placed in a shared assembly (Just like it is common with .NET remoting)

```csharp
namespace HelloWorld.Shared
{
    public interface ISayHelloService
    {
        event Action<string, string> MessageReceived;
        
        void Say(string name, string message);
    }
}
```
### Server
The server side application provides services to clients.

```csharp
using System;
using CoreRemoting;
using CoreRemoting.DependencyInjection;
using HelloWorld.Shared;

namespace HelloWorld.Server
{
    public class SayHelloService : ISayHelloService
    {
        // Event to notify clients when users post new chat messages
        public event Action<string, string> MessageReceived;
        
        // Call via RPC to say something in the chat 
        public void Say(string name, string message)
        {
            MessageReceived?.Invoke(name, message);
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
                    // Make SayHelloSevice class available for RPC calls from clients
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
The client consumes remote services hosted on the server.

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

            // Create a proxy of the remote service, which behaves almost like a regular local object
            var proxy = client.CreateProxy<ISayHelloService>();
            
            // Receive chat messages send by other remote users by event
            proxy.MessageReceived += (senderName, message) => 
                Console.WriteLine($"\n  {senderName} says: {message}\n");
            
            Console.WriteLine("What's your name?");
            var name = Console.ReadLine();

            Console.WriteLine("\nEntered chat. Type 'quit' to leave.");

            bool quit = false;

            while (!quit)
            {
                var text = Console.ReadLine();

                if (text != null && text.Equals("quit", StringComparison.InvariantCultureIgnoreCase))
                    quit = true;
                else
                {
                    // Post a new chat message
                    proxy.Say(name, text);
                }
            }
        }
    }
}
```
Source code of this example is also available in the repository at https://github.com/theRainbird/CoreRemoting/tree/master/Examples/HelloWorld.

To test the hello world solution, start the server (HelloWorld.Server) and then multiple clients (HelloWorld.Client).
Have fun.

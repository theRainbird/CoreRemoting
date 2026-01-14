## Overview

CoreRemoting is a library that helps develop distributed applications that use RPC communication. 
The **server** part of the application provides **services** for multiple **clients**. 

A service is a class that implement an interface which is shared between server and client. 
Sharing means that the **service interface** is defined in separate assembly that is deployed on server and also on client. 
Clients can call methods of the provided services remotely over the network (**R**emote **P**rocedure **C**all). 

Because the client only knows the shared interface of the service, but not the service implementation, a proxy for the service is needed. 
All methods that are called on such a **proxy**, are serialized _(see [Serialization](https://github.com/theRainbird/CoreRemoting/wiki/Serialization) chapter for details)_ and sent over the network to the server. On server side the call is deserialized and dispatched to the real service implementation. After successful execution of the service method, the result is serialized and sent back to the calling client.

To bring that RPC thing to work, CoreRemoting needs the following components:

**Interfaces of CoreRemoting server components**

![Remoting Server Structure](https://raw.githubusercontent.com/theRainbird/CoreRemoting/master/docs/images/RemotingServer_Structure.png)

What implementations of this component interfaces a CoreRemoting server instance should use, is defined in it's configuration, which is done via a ServerConfig object. Most properties of the **[ServerConfig](https://github.com/theRainbird/CoreRemoting/wiki/Configuration#serverconfig)** class have default values. If, for example, no channel is explicitly specified in the configuration, a duplex TCP channel is used as default.  

To create a new server instance which is configured to to host the SayHelloService class as service,  using ISayHelloService as public service interface and listen on localhost at port 9090, the following C# code is needed:

```C#
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
```
**Interfaces of CoreRemoting client components**

![Remoting Client Structure](https://raw.githubusercontent.com/theRainbird/CoreRemoting/master/docs/images/RemotingClient_Structure.png)

On client side, there is also configuration needed, which is done via a **[ClientConfig](https://github.com/theRainbird/CoreRemoting/wiki/Configuration#clientconfig)** object.

To connect to the server, the following code is needed on client side:
```C#
    using var client = new RemotingClient(new ClientConfig()
    {
        ServerHostName = "localhost",
        ServerPort = 9090
    });
    
    client.Connect();
```
After the client instance is connected to the server, a proxy can be created to call methods on a remote service.  
```C#
    ISayHelloService proxy = client.CreateProxy<ISayHelloService>();
```
The diagram below shows the RPC call flow of the **["Hello World"](https://github.com/theRainbird/CoreRemoting/tree/master/Examples/HelloWorld)** example application.

![Hello World Application as Diagram](https://raw.githubusercontent.com/theRainbird/CoreRemoting/master/docs/images/HelloWorld_AsDiagram.png)

Please also read the [security documentation page](https://github.com/theRainbird/CoreRemoting/wiki/Security) to learn how to secure your CoreRemoting application.
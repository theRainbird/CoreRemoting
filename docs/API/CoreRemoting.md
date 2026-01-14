# CoreRemoting Namespace API Reference

This namespace contains the main API components for CoreRemoting, including the core client and server classes, configuration, and fundamental interfaces.

## Core Classes

### 🏗️ RemotingClient
**Namespace:** `CoreRemoting`  
**Interfaces:** `IRemotingClient`, `IAsyncDisposable`, `IDisposable`

Provides remoting functionality on the client side. This is the main entry point for client-side RPC operations.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `Config` | `ClientConfig` | Configuration settings used by this client instance |
| `PublicKey` | `byte[]` | Public key of this client instance (used for encryption) |
| `InvocationTimeout` | `int?` | Invocation timeout in milliseconds (null for infinite) |
| `MessageEncryption` | `bool` | Whether messages should be encrypted |
| `IsConnected` | `bool` | Whether the connection to the server is established |
| `HasSession` | `bool` | Whether this client instance has an active session |
| `Identity` | `RemotingIdentity` | Authenticated identity (null if not authenticated) |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Connect()` | `void` | Connects to the configured server |
| `ConnectAsync()` | `Task` | Asynchronously connects to the configured server |
| `Disconnect(bool quiet = false)` | `void` | Disconnects from the server |
| `DisconnectAsync(bool quiet = false)` | `Task` | Asynchronously disconnects from the server |
| `CreateProxy<T>(string serviceName = "")` | `T` | Creates a proxy for a remote service |
| `CreateProxy(Type serviceInterfaceType, string serviceName = "")` | `object` | Creates a proxy using reflection |
| `CreateProxy(ServiceReference serviceReference)` | `object` | Creates a proxy from service reference |
| `ShutdownProxy(object serviceProxy)` | `void` | Shuts down a service proxy and frees resources |

#### Events

| Event | Description |
|-------|-------------|
| `AfterDisconnect` | Fires after client was disconnected |

#### Static Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `GetActiveClientInstance(string uniqueClientInstanceName)` | `IRemotingClient` | Gets an active client instance by name |

#### Static Properties

| Property | Type | Description |
|----------|------|-------------|
| `DefaultRemotingClient` | `IRemotingClient` | Gets or sets the default client instance |
| `ActiveClientInstances` | `IEnumerable<IRemotingClient>` | Gets all active client instances |

#### Usage Examples

```csharp
// Basic usage
using var client = new RemotingClient(new ClientConfig()
{
    ServerHostName = "localhost",
    ServerPort = 9090
});

client.Connect();

// Create proxy and call remote service
var calculator = client.CreateProxy<ICalculator>();
int result = calculator.Add(5, 3);
```

```csharp
// With authentication
using var client = new RemotingClient(new ClientConfig()
{
    ServerHostName = "localhost",
    ServerPort = 9090,
    Credentials = new[] { new Credential("username", "password") }
});

client.Connect();
```

```csharp
// Asynchronous usage
using var client = new RemotingClient(config);
await client.ConnectAsync();

var service = client.CreateProxy<IMyService>();
var result = await service.GetDataAsync();
```

---

### 🏗️ RemotingServer
**Namespace:** `CoreRemoting`  
**Interfaces:** `IRemotingServer`, `IAsyncDisposable`, `IDisposable`

CoreRemoting server implementation that hosts services and handles client connections.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `Config` | `ServerConfig` | Configuration settings for this server instance |
| `ServiceRegistry` | `IDependencyInjectionContainer` | DI container used as service registry |
| `UniqueServerInstanceName` | `string` | Unique name of this server instance |
| `Serializer` | `ISerializerAdapter` | Configured serializer for message serialization |
| `MethodCallMessageBuilder` | `MethodCallMessageBuilder` | Message builder utility |
| `MessageEncryptionManager` | `IMessageEncryptionManager` | Message encryption component |
| `SessionRepository` | `ISessionRepository` | Session management repository |
| `Channel` | `IServerChannel` | Network transport channel |

#### Static Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `GetActiveServerInstance(string uniqueServerInstanceName)` | `IRemotingServer` | Gets an active server instance by name |

#### Static Properties

| Property | Type | Description |
|----------|------|-------------|
| `DefaultRemotingServer` | `IRemotingServer` | Gets or sets the default server instance |
| `ActiveServerInstances` | `IEnumerable<IRemotingServer>` | Gets all active server instances |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Start()` | `void` | Starts the server and begins listening for connections |
| `Stop()` | `void` | Stops the server and disconnects all clients |
| `Dispose()` | `void` | Frees managed resources |
| `DisposeAsync()` | `ValueTask` | Asynchronously frees managed resources |

#### Events

| Event | Event Args | Description |
|-------|------------|-------------|
| `Logon` | `EventArgs` | Fires when a client logs on |
| `Logoff` | `EventArgs` | Fires when a client logs off |
| `BeginCall` | `ServerRpcContext` | Fires when an RPC call is prepared (can cancel) |
| `RejectCall` | `ServerRpcContext` | Fires when an RPC call is rejected |
| `BeforeCall` | `ServerRpcContext` | Fires before an RPC call is invoked |
| `AfterCall` | `ServerRpcContext` | Fires after an RPC call is invoked |
| `Error` | `Exception` | Fires if an error occurs |

#### Usage Examples

```csharp
// Basic server setup
using var server = new RemotingServer(new ServerConfig()
{
    HostName = "localhost",
    NetworkPort = 9090,
    RegisterServicesAction = container =>
    {
        container.RegisterService<ICalculatorService, CalculatorService>(
            ServiceLifetime.Singleton);
    }
});

server.Start();
```

```csharp
// With event handling
var server = new RemotingServer(config);

server.BeforeCall += (sender, context) =>
{
    Console.WriteLine($"Calling {context.MethodName}");
};

server.AfterCall += (sender, context) =>
{
    Console.WriteLine($"Completed {context.MethodName}");
};

server.Start();
```

```csharp
// With custom channel and serializer
using var server = new RemotingServer(new ServerConfig()
{
    HostName = "localhost",
    NetworkPort = 9090,
    Channel = new WebsocketServerChannel(),
    Serializer = new NeoBinarySerializerAdapter(),
    RegisterServicesAction = container =>
    {
        container.RegisterService<IMyService, MyService>(ServiceLifetime.Singleton);
    }
});

server.Start();
```

---

## Configuration Classes

### ⚙️ ClientConfig
**Namespace:** `CoreRemoting`

Configuration settings for CoreRemoting client instances.

#### Key Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `UniqueClientInstanceName` | `string` | Auto-generated | Unique name of the client instance |
| `ConnectionTimeout` | `int` | 120 | Connection timeout in seconds (0 = infinite) |
| `AuthenticationTimeout` | `int` | 30 | Authentication timeout in seconds (0 = infinite) |
| `InvocationTimeout` | `int` | 0 | Invocation timeout in seconds (0 = infinite) |
| `ServerHostName` | `string` | "localhost" | Server host name or IP address |
| `ServerPort` | `int` | 9090 | Server network port |
| `Serializer` | `ISerializerAdapter` | `BsonSerializerAdapter` | Serializer to be used |
| `MessageEncryption` | `bool` | true | Whether to encrypt messages |
| `KeySize` | `int` | 4096 | RSA key size for encryption |
| `Channel` | `IClientChannel` | `TcpClientChannel` | Network communication channel |
| `ChannelConnectionName` | `string` | null | Channel connection name (e.g., pipe name for Named Pipe channel) |
| `Credentials` | `Credential[]` | null | Authentication credentials |
| `KeepSessionAliveInterval` | `int` | 20 | Session keep-alive interval in seconds |
| `IsDefault` | `bool` | false | Whether this is the default client instance |

#### Usage Examples

```csharp
var config = new ClientConfig()
{
    ServerHostName = "myserver.com",
    ServerPort = 8080,
    MessageEncryption = true,
    KeySize = 2048,
    Credentials = new[]
    {
        new Credential { Name = "username", Value = "password" }
    }
};

var client = new RemotingClient(config);
```

```csharp
// Named Pipe configuration (for same-machine communication)
var config = new ClientConfig()
{
    Channel = new NamedPipeClientChannel(),
    ChannelConnectionName = "MyAppPipe",
    MessageEncryption = true
};

var client = new RemotingClient(config);
```

---

### ⚙️ ServerConfig
**Namespace:** `CoreRemoting`

Configuration settings for CoreRemoting server instances.

#### Key Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `HostName` | `string` | "localhost" | Host name or IP address to listen on |
| `NetworkPort` | `int` | 9090 | Network port to listen on |
| `MessageEncryption` | `bool` | true | Whether to encrypt communication |
| `KeySize` | `int` | 4096 | RSA key size for encryption |
| `Serializer` | `ISerializerAdapter` | `BsonSerializerAdapter` | Serializer to be used |
| `DependencyInjectionContainer` | `IDependencyInjectionContainer` | `CastleWindsorDependencyInjectionContainer` | DI container |
| `RegisterServicesAction` | `Action<IDependencyInjectionContainer>` | null | Action to register services |
| `SessionRepository` | `ISessionRepository` | `SessionRepository` | Session repository |
| `Channel` | `IServerChannel` | `TcpServerChannel` | Network communication channel |
| `ChannelConnectionName` | `string` | null | Channel connection name (e.g., pipe name for Named Pipe channel) |
| `AuthenticationProvider` | `IAuthenticationProvider` | null | Authentication provider |
| `AuthenticationRequired` | `bool` | false | Whether authentication is required |
| `UniqueServerInstanceName` | `string` | Auto-generated | Unique name of server instance |
| `InactiveSessionSweepInterval` | `int` | 60 | Session sweep interval in seconds |
| `MaximumSessionInactivityTime` | `int` | 30 | Maximum session inactivity in minutes |
| `IsDefault` | `bool` | false | Whether this is the default server instance |

#### Usage Examples

```csharp
var config = new ServerConfig()
{
    HostName = "0.0.0.0",
    NetworkPort = 8080,
    MessageEncryption = true,
    AuthenticationRequired = true,
    AuthenticationProvider = new MyAuthProvider(),
    RegisterServicesAction = container =>
    {
        container.RegisterService<IMyService, MyService>(ServiceLifetime.Singleton);
    }
};

var server = new RemotingServer(config);
```

```csharp
// Named Pipe server configuration (for same-machine communication)
var config = new ServerConfig()
{
    Channel = new NamedPipeServerChannel(),
    ChannelConnectionName = "MyAppPipe",
    MessageEncryption = true,
    RegisterServicesAction = container =>
    {
        container.RegisterService<ILocalService, LocalService>(ServiceLifetime.Singleton);
    }
};

var server = new RemotingServer(config);
```

---

## Core Interfaces

### 🔄 IRemotingClient
**Namespace:** `CoreRemoting`

Interface defining the contract for CoreRemoting client implementations.

#### Key Members

| Member | Type | Description |
|--------|------|-------------|
| `Config` | `ClientConfig` | Configuration settings |
| `PublicKey` | `byte[]` | Public key for encryption |
| `InvocationTimeout` | `int?` | Invocation timeout in milliseconds |
| `MessageEncryption` | `bool` | Whether messages are encrypted |
| `IsConnected` | `bool` | Connection status |
| `HasSession` | `bool` | Session status |
| `CreateProxy<T>(string serviceName)` | `T` | Creates service proxy |
| `CreateProxy(Type, string)` | `object` | Creates proxy by type |
| `ShutdownProxy(object)` | `void` | Shuts down proxy |
| `Connect()` | `void` | Connects to server |
| `Disconnect(bool)` | `void` | Disconnects from server |
| `AfterDisconnect` | `event Action` | Fires after disconnect |

**Implemented by:** `RemotingClient`

---

### 🔄 IRemotingServer
**Namespace:** `CoreRemoting`

Interface defining the contract for CoreRemoting server implementations.

#### Key Members

| Member | Type | Description |
|--------|------|-------------|
| `Config` | `ServerConfig` | Configuration settings |
| `ServiceRegistry` | `IDependencyInjectionContainer` | Service registry |
| `UniqueServerInstanceName` | `string` | Unique server name |
| `Serializer` | `ISerializerAdapter` | Configured serializer |
| `MethodCallMessageBuilder` | `MethodCallMessageBuilder` | Message builder |
| `MessageEncryptionManager` | `IMessageEncryptionManager` | Encryption manager |
| `SessionRepository` | `ISessionRepository` | Session repository |
| `Channel` | `IServerChannel` | Network channel |
| `Logon` | `event EventHandler` | Client logon event |
| `Logoff` | `event EventHandler` | Client logoff event |
| `BeginCall` | `event EventHandler<ServerRpcContext>` | Call preparation event |
| `RejectCall` | `event EventHandler<ServerRpcContext>` | Call rejection event |
| `BeforeCall` | `event EventHandler<ServerRpcContext>` | Before call event |
| `AfterCall` | `event EventHandler<ServerRpcContext>` | After call event |
| `Error` | `event EventHandler<Exception>` | Error event |

**Implemented by:** `RemotingServer`

---

### 🔄 IServiceProxy
**Namespace:** `CoreRemoting`

Interface for service proxy implementations that handle remote method invocation.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Invoke(MethodInfo method, object[] args)` | `object` | Invokes a remote method |
| `InvokeAsync(MethodInfo method, object[] args)` | `Task<object>` | Invokes a remote method asynchronously |
| `Shutdown()` | `void` | Shuts down the proxy |

**Implemented by:** `ServiceProxy<TServiceInterface>`

---

## Service Proxy Implementation

### 🏗️ ServiceProxy<TServiceInterface>
**Namespace:** `CoreRemoting`

Generic service proxy implementation that intercepts method calls and routes them through the remoting infrastructure.

#### Type Parameters

| Parameter | Description |
|-----------|-------------|
| `TServiceInterface` | Service interface type that the proxy implements |

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `Client` | `IRemotingClient` | Associated remoting client |
| `ServiceName` | `string` | Name of the remote service |
| `UniqueCallKey` | `Guid` | Unique identifier for this proxy instance |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Invoke(MethodInfo method, object[] args)` | `object` | Invokes remote method synchronously |
| `InvokeAsync(MethodInfo method, object[] args)` | `Task<object>` | Invokes remote method asynchronously |
| `Shutdown()` | `void` | Shuts down the proxy |

#### Usage Examples

```csharp
// Proxy is typically created through RemotingClient
var client = new RemotingClient(config);
client.Connect();

// This creates a ServiceProxy<IMyService> internally
var proxy = client.CreateProxy<IMyService>();

// Proxy intercepts method calls
var result = proxy.SomeMethod(param1, param2);
```

---

## Session Management

### 🏗️ RemotingSession
**Namespace:** `CoreRemoting`

Represents a client session on the server side, containing connection and authentication information.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `SessionId` | `Guid` | Unique session identifier |
| `ClientPublicKey` | `byte[]` | Client's public key |
| `SharedSecret` | `byte[]` | Shared secret for encryption |
| `Identity` | `RemotingIdentity` | Authenticated identity |
| `LastActivity` | `DateTime` | Last activity timestamp |
| `IsAuthenticated` | `bool` | Authentication status |

---

### 🏗️ SessionRepository
**Namespace:** `CoreRemoting`

Default implementation of session repository that manages client sessions.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `CreateSession(byte[] clientPublicKey, byte[] sharedSecret)` | `RemotingSession` | Creates new session |
| `GetSession(Guid sessionId)` | `RemotingSession` | Gets session by ID |
| `RemoveSession(Guid sessionId)` | `bool` | Removes session |
| `SweepInactiveSessions()` | `int` | Removes inactive sessions |

---

### 🔄 ISessionRepository
**Namespace:** `CoreRemoting`

Interface for session repository implementations.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `CreateSession(byte[], byte[])` | `RemotingSession` | Creates new session |
| `GetSession(Guid)` | `RemotingSession` | Gets session by ID |
| `RemoveSession(Guid)` | `bool` | Removes session |
| `SweepInactiveSessions()` | `int` | Sweeps inactive sessions |

**Implemented by:** `SessionRepository`

---

## RPC Context Classes

### 🏗️ ClientRpcContext
**Namespace:** `CoreRemoting`

Context object for client-side RPC calls, tracking call state and results.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `UniqueCallKey` | `Guid` | Unique call identifier |
| `ResultMessage` | `MethodCallResultMessage` | Result message from server |
| `Error` | `bool` | Whether the call resulted in an error |
| `RemoteException` | `Exception` | Remote exception if error occurred |
| `TaskSource` | `TaskCompletionSource<bool>` | Task completion source |

---

### 🏗️ ServerRpcContext
**Namespace:** `CoreRemoting`

Context object for server-side RPC calls, containing call information and state.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `SessionId` | `Guid` | Session identifier |
| `MethodName` | `string` | Name of the method being called |
| `Parameters` | `object[]` | Method parameters |
| `Result` | `object` | Method result |
| `Exception` | `Exception` | Exception if error occurred |
| `IsCanceled` | `bool` | Whether the call was canceled |

---

## Call Context

### 🏗️ CallContext
**Namespace:** `CoreRemoting`

Provides implicit data flow across remoting boundaries, allowing context data to be automatically propagated with RPC calls.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `SetData(string key, object value)` | `void` | Sets context data |
| `GetData(string key)` | `object` | Gets context data |
| `GetData<T>(string key)` | `T` | Gets context data as type T |
| `Clear()` | `void` | Clears all context data |

#### Usage Examples

```csharp
// On client side, before RPC call
CallContext.SetData("UserId", 123);
CallContext.SetData("TenantId", "tenant-abc");

// This data will be automatically sent with RPC calls
var result = remoteService.DoWork();

// On server side, the context is available
var userId = CallContext.GetData<int>("UserId");
var tenantId = CallContext.GetData<string>("TenantId");
```

---

### 🏗️ CallContextEntry
**Namespace:** `CoreRemoting`

Represents a single entry in the call context.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `Key` | `string` | Context entry key |
| `Value` | `object` | Context entry value |
| `TypeName` | `string` | Type name of the value |

---

## Exception Classes

### 🚫 RemotingException
**Namespace:** `CoreRemoting`

Base exception class for CoreRemoting-related errors.

#### Constructors

| Constructor | Description |
|-------------|-------------|
| `RemotingException()` | Creates new instance |
| `RemotingException(string message)` | Creates with message |
| `RemotingException(string message, Exception innerException)` | Creates with message and inner exception |

---

### 🚫 RemoteInvocationException
**Namespace:** `CoreRemoting`

Exception thrown when a remote method invocation fails.

#### Constructors

| Constructor | Description |
|-------------|-------------|
| `RemoteInvocationException()` | Creates new instance |
| `RemoteInvocationException(string message)` | Creates with message |
| `RemoteInvocationException(string message, Exception innerException)` | Creates with message and inner exception |

---

## Attributes

### 🏷️ OneWayAttribute
**Namespace:** `CoreRemoting`

Marks a method as one-way, meaning the client doesn't wait for a response from the server.

#### Usage Examples

```csharp
[OneWay]
void LogMessage(string message);

[OneWay]
void SendNotification(NotificationData data);
```

---

### 🏷️ ReturnAsProxyAttribute
**Namespace:** `CoreRemoting`

Marks a method to return a proxy to another remote service instead of the actual object.

#### Usage Examples

```csharp
[ReturnAsProxy]
IUserService GetUserService();

[ReturnAsProxy]
IDataService GetDataService(string dataSource);
```

---

## Utility Classes

### 🏗️ RemotingProxyBuilder
**Namespace:** `CoreRemoting`

Utility class for creating remoting proxies.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `CreateProxy<T>(IRemotingClient client, string serviceName)` | `T` | Creates typed proxy |
| `CreateProxy(IRemotingClient client, Type interfaceType, string serviceName)` | `object` | Creates proxy by type |

---

### 🔧 MicrosoftDependencyInjectionExtensionMethods
**Namespace:** `CoreRemoting`

Extension methods for integrating CoreRemoting with Microsoft's dependency injection container.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `AddCoreRemotingClient(this IServiceCollection services, Action<ClientConfig> configure)` | `IServiceCollection` | Registers CoreRemoting client |
| `AddCoreRemotingServer(this IServiceCollection services, Action<ServerConfig> configure)` | `IServiceCollection` | Registers CoreRemoting server |

#### Usage Examples

```csharp
// In Startup.cs or Program.cs
services.AddCoreRemotingClient(clientConfig =>
{
    clientConfig.ServerHostName = "localhost";
    clientConfig.ServerPort = 9090;
});

services.AddCoreRemotingServer(serverConfig =>
{
    serverConfig.NetworkPort = 9090;
    serverConfig.RegisterServicesAction = container =>
    {
        container.RegisterService<IMyService, MyService>(ServiceLifetime.Singleton);
    };
});
```

---

## See Also

- [CoreRemoting.Channels](CoreRemoting-Channels.md) - Transport layer implementations
- [CoreRemoting.Serialization](CoreRemoting-Serialization.md) - Serialization framework
- [CoreRemoting.Authentication](CoreRemoting-Authentication.md) - Authentication framework
- [Configuration](../Configuration.md) - Configuration details
- [Overview](../Overview.md) - Getting started guide
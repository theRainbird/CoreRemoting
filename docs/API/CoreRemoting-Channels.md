# CoreRemoting.Channels Namespace API Reference

This namespace contains the transport layer implementations for CoreRemoting, providing different communication protocols and channels for client-server communication.

## Core Interfaces

### 🔄 IClientChannel
**Namespace:** `CoreRemoting.Channels`  
**Interfaces:** `IAsyncDisposable`

Interface for CoreRemoting client-side transport channels. Defines the contract for establishing and managing client connections to remote servers.

#### Key Members

| Member | Type | Description |
|--------|------|-------------|
| `Init(IRemotingClient client)` | `void` | Initializes the channel with client configuration |
| `ConnectAsync()` | `Task` | Establishes connection with the server |
| `DisconnectAsync()` | `Task` | Closes the connection |
| `IsConnected` | `bool` | Gets whether connection is established |
| `RawMessageTransport` | `IRawMessageTransport` | Gets the raw message transport component |
| `Disconnected` | `event Action` | Fires when server disconnects |

#### Usage Examples

```csharp
// Creating a custom client channel
public class MyCustomClientChannel : IClientChannel
{
    public void Init(IRemotingClient client)
    {
        // Initialize with client configuration
    }
    
    public async Task ConnectAsync()
    {
        // Establish connection logic
    }
    
    // Implement other required members...
}
```

**Implemented by:** `TcpClientChannel`, `WebsocketClientChannel`, `NamedPipeClientChannel`, `NullClientChannel`

---

### 🔄 IServerChannel
**Namespace:** `CoreRemoting.Channels`  
**Interfaces:** `IAsyncDisposable`

Interface for CoreRemoting server-side transport channels. Defines the contract for listening for and accepting client connections.

#### Key Members

| Member | Type | Description |
|--------|------|-------------|
| `Init(IRemotingServer server)` | `void` | Initializes the channel with server configuration |
| `StartListening()` | `void` | Starts listening for client requests |
| `StopListening()` | `void` | Stops listening for client requests |
| `IsListening` | `bool` | Gets whether channel is listening |

#### Usage Examples

```csharp
// Creating a custom server channel
public class MyCustomServerChannel : IServerChannel
{
    public void Init(IRemotingServer server)
    {
        // Initialize with server configuration
    }
    
    public void StartListening()
    {
        // Start listening logic
    }
    
    // Implement other required members...
}
```

**Implemented by:** `TcpServerChannel`, `WebsocketServerChannel`, `NamedPipeServerChannel`, `NullServerChannel`

---

### 🔄 IRawMessageTransport
**Namespace:** `CoreRemoting.Channels`

Interface for raw message transport components. Provides the lowest-level abstraction for sending and receiving message data over the network.

#### Key Members

| Member | Type | Description |
|--------|------|-------------|
| `ReceiveMessage` | `event Action<byte[]>` | Fires when a message is received |
| `ErrorOccured` | `event Action<string, Exception>` | Fires when an error occurs |
| `LastException` | `NetworkException` | Gets or sets the last exception |
| `SendMessageAsync(byte[] rawMessage)` | `Task<bool>` | Sends a message asynchronously |

#### Usage Examples

```csharp
// Creating a custom message transport
public class MyCustomTransport : IRawMessageTransport
{
    public event Action<byte[]> ReceiveMessage;
    public event Action<string, Exception> ErrorOccured;
    
    public NetworkException LastException { get; set; }
    
    public async Task<bool> SendMessageAsync(byte[] rawMessage)
    {
        // Send message implementation
        return true;
    }
    
    // Implement other required members...
}
```

**Implemented by:** `TcpClientChannel`, `TcpConnection`, `WebsocketTransport`, `NamedPipeTransport`, `NullTransport`

---

## TCP Channel Implementation

### 🏗️ TcpClientChannel
**Namespace:** `CoreRemoting.Channels.Tcp`  
**Interfaces:** `IClientChannel`, `IRawMessageTransport`

Client-side TCP channel implementation using WatsonTcp. Provides reliable TCP-based communication for CoreRemoting clients.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsConnected` | `bool` | Gets whether TCP connection is established |
| `RawMessageTransport` | `IRawMessageTransport` | Returns this channel as message transport |
| `LastException` | `NetworkException` | Gets or sets the last exception |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Init(IRemotingClient client)` | `void` | Initializes with client configuration |
| `ConnectAsync()` | `Task` | Connects to TCP server |
| `DisconnectAsync()` | `Task` | Disconnects from TCP server |
| `SendMessageAsync(byte[] rawMessage)` | `Task<bool>` | Sends message to server |

#### Events

| Event | Description |
|-------|-------------|
| `ReceiveMessage` | Fires when message received from server |
| `ErrorOccured` | Fires when TCP error occurs |
| `Disconnected` | Fires when server disconnects |

#### Configuration

The TCP client channel is configured through `ClientConfig`:

```csharp
var config = new ClientConfig()
{
    ServerHostName = "localhost",
    ServerPort = 9090,
    Channel = new TcpClientChannel()
};

var client = new RemotingClient(config);
```

---

### 🏗️ TcpServerChannel
**Namespace:** `CoreRemoting.Channels.Tcp`  
**Interfaces:** `IServerChannel`

Server-side TCP channel implementation using WatsonTcp. Listens for TCP connections and handles multiple concurrent clients.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsListening` | `bool` | Gets whether server is listening |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Init(IRemotingServer server)` | `void` | Initializes with server configuration |
| `StartListening()` | `void` | Starts listening for connections |
| `StopListening()` | `void` | Stops listening and disconnects clients |

#### Configuration

```csharp
var config = new ServerConfig()
{
    HostName = "localhost",
    NetworkPort = 9090,
    Channel = new TcpServerChannel()
};

var server = new RemotingServer(config);
```

---

### 🏗️ TcpConnection
**Namespace:** `CoreRemoting.Channels.Tcp`  
**Interfaces:** `IRawMessageTransport`

Represents an individual TCP connection on the server side. Handles message exchange with a specific client.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsConnected` | `bool` | Gets whether connection is active |
| `ClientGuid` | `Guid` | Unique identifier for the client |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `SendMessageAsync(byte[] rawMessage)` | `Task<bool>` | Sends message to client |
| `Disconnect()` | `void` | Disconnects the client |

#### Events

| Event | Description |
|-------|-------------|
| `ReceiveMessage` | Fires when message received from client |
| `ErrorOccured` | Fires when connection error occurs |
| `Disconnected` | Fires when client disconnects |

---

## WebSocket Channel Implementation

### 🏗️ WebsocketClientChannel
**Namespace:** `CoreRemoting.Channels.Websocket`  
**Interfaces:** `IClientChannel`, `IRawMessageTransport`

Client-side WebSocket channel implementation. Suitable for web-based applications and environments requiring HTTP/WebSocket compatibility.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsConnected` | `bool` | Gets whether WebSocket connection is established |
| `RawMessageTransport` | `IRawMessageTransport` | Returns this channel as message transport |
| `LastException` | `NetworkException` | Gets or sets the last exception |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Init(IRemotingClient client)` | `void` | Initializes with client configuration |
| `ConnectAsync()` | `Task` | Connects to WebSocket server |
| `DisconnectAsync()` | `Task` | Disconnects from WebSocket server |
| `SendMessageAsync(byte[] rawMessage)` | `Task<bool>` | Sends message to server |

#### Configuration

```csharp
var config = new ClientConfig()
{
    ServerHostName = "localhost",
    ServerPort = 9090,
    Channel = new WebsocketClientChannel()
};

var client = new RemotingClient(config);
```

---

### 🏗️ WebsocketServerChannel
**Namespace:** `CoreRemoting.Channels.Websocket`  
**Interfaces:** `IServerChannel`

Server-side WebSocket channel implementation. Handles WebSocket connections and provides HTTP/WebSocket compatibility.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsListening` | `bool` | Gets whether server is listening |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Init(IRemotingServer server)` | `void` | Initializes with server configuration |
| `StartListening()` | `void` | Starts listening for WebSocket connections |
| `StopListening()` | `void` | Stops listening and disconnects clients |

#### Configuration

```csharp
var config = new ServerConfig()
{
    HostName = "localhost",
    NetworkPort = 9090,
    Channel = new WebsocketServerChannel()
};

var server = new RemotingServer(config);
```

---

### 🏗️ WebsocketServerConnection
**Namespace:** `CoreRemoting.Channels.Websocket`  
**Interfaces:** `IRawMessageTransport`

Represents an individual WebSocket connection on the server side.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsConnected` | `bool` | Gets whether connection is active |
| `ConnectionId` | `string` | Unique identifier for the WebSocket connection |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `SendMessageAsync(byte[] rawMessage)` | `Task<bool>` | Sends message to WebSocket client |
| `Disconnect()` | `void` | Disconnects the WebSocket client |

---

### 🏗️ WebSocketTransport
**Namespace:** `CoreRemoting.Channels.Websocket`  
**Interfaces:** `IRawMessageTransport`

Low-level WebSocket transport implementation for handling WebSocket message exchange.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsConnected` | `bool` | Gets whether WebSocket transport is active |
| `LastException` | `NetworkException` | Gets or sets the last exception |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `SendMessageAsync(byte[] rawMessage)` | `Task<bool>` | Sends message via WebSocket |
| `ConnectAsync(string url)` | `Task` | Connects to WebSocket URL |

---

## Named Pipe Channel Implementation

### 🏗️ NamedPipeClientChannel
**Namespace:** `CoreRemoting.Channels.NamedPipe`  
**Interfaces:** `IClientChannel`, `IRawMessageTransport`

Client-side named pipe channel implementation. Suitable for same-machine communication scenarios and Windows environments.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsConnected` | `bool` | Gets whether named pipe connection is established |
| `RawMessageTransport` | `IRawMessageTransport` | Returns this channel as message transport |
| `LastException` | `NetworkException` | Gets or sets the last exception |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Init(IRemotingClient client)` | `void` | Initializes with client configuration |
| `ConnectAsync()` | `Task` | Connects to named pipe server |
| `DisconnectAsync()` | `Task` | Disconnects from named pipe |
| `SendMessageAsync(byte[] rawMessage)` | `Task<bool>` | Sends message through named pipe |

#### Configuration

```csharp
var config = new ClientConfig()
{
    Channel = new NamedPipeClientChannel(),
    ChannelConnectionName = "uniquepipename" // The name of the pipe
    // Note: Named pipes typically don't use host/port
};

var client = new RemotingClient(config);
```

---

### 🏗️ NamedPipeServerChannel
**Namespace:** `CoreRemoting.Channels.NamedPipe`  
**Interfaces:** `IServerChannel`

Server-side named pipe channel implementation. Creates and manages named pipe connections for same-machine communication.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsListening` | `bool` | Gets whether server is listening |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Init(IRemotingServer server)` | `void` | Initializes with server configuration |
| `StartListening()` | `void` | Starts listening for pipe connections |
| `StopListening()` | `void` | Stops listening and disconnects clients |

#### Configuration

```csharp
var config = new ServerConfig()
{
    Channel = new NamedPipeServerChannel(),
    ChannelConnectionName = "uniquepipename" // The name of the pipe
};

var server = new RemotingServer(config);
```

---

### 🏗️ SimpleNamedPipeServer
**Namespace:** `CoreRemoting.Channels.NamedPipe`

Utility class for simplified named pipe server operations.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `CreatePipe(string pipeName)` | `NamedPipeServerStream` | Creates a named pipe server stream |
| `WaitForConnectionAsync(NamedPipeServerStream pipe)` | `Task` | Waits for client connection |

---

## Null Channel Implementation (Testing)

### 🏗️ NullClientChannel
**Namespace:** `CoreRemoting.Channels.Null`  
**Interfaces:** `IClientChannel`, `IRawMessageTransport`

Testing channel implementation that doesn't perform any actual network operations. Useful for unit testing and integration testing without network dependencies.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsConnected` | `bool` | Always returns true after "connection" |
| `RawMessageTransport` | `IRawMessageTransport` | Returns this channel as message transport |
| `LastException` | `NetworkException` | Gets or sets the last exception |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Init(IRemotingClient client)` | `void` | Initializes channel (no actual initialization) |
| `ConnectAsync()` | `Task` | "Connects" immediately without network operation |
| `DisconnectAsync()` | `Task` | "Disconnects" immediately |
| `SendMessageAsync(byte[] rawMessage)` | `Task<bool>` | Returns true without sending |

#### Usage Examples

```csharp
// Testing without network
var config = new ClientConfig()
{
    Channel = new NullClientChannel()
};

var client = new RemotingClient(config);
client.Connect(); // Instant, no network required
```

---

### 🏗️ NullServerChannel
**Namespace:** `CoreRemoting.Channels.Null`  
**Interfaces:** `IServerChannel`

Testing server channel that doesn't perform actual network operations.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsListening` | `bool` | Gets whether server is "listening" |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Init(IRemotingServer server)` | `void` | Initializes server (no actual initialization) |
| `StartListening()` | `void` | "Starts listening" immediately |
| `StopListening()` | `void` | "Stops listening" immediately |

---

### 🏗️ NullTransport
**Namespace:** `CoreRemoting.Channels.Null`  
**Interfaces:** `IRawMessageTransport`

Testing message transport that doesn't perform actual message transmission.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `LastException` | `NetworkException` | Gets or sets the last exception |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `SendMessageAsync(byte[] rawMessage)` | `Task<bool>` | Returns true without sending |

---

## Exception Classes

### 🚫 NetworkException
**Namespace:** `CoreRemoting.Channels`

Base exception for network-related errors in the channel layer.

#### Constructors

| Constructor | Description |
|-------------|-------------|
| `NetworkException()` | Creates new instance |
| `NetworkException(string message)` | Creates with message |
| `NetworkException(string message, Exception innerException)` | Creates with message and inner exception |

#### Usage Examples

```csharp
try
{
    await client.ConnectAsync();
}
catch (NetworkException ex)
{
    Console.WriteLine($"Network error: {ex.Message}");
}
```

---

## See Also

- [CoreRemoting](CoreRemoting.md) - Core API classes
- [CoreRemoting.Serialization](CoreRemoting-Serialization.md) - Message serialization
- [CoreRemoting.Authentication](CoreRemoting-Authentication.md) - Authentication framework
- [Configuration](../Configuration.md) - Channel configuration options
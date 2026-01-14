# CoreRemoting API Reference

This section provides comprehensive API documentation for all public classes, interfaces, and components in the CoreRemoting framework. The documentation is organized by namespace to help you quickly find the specific components you need.

## Quick Navigation

### Core API
- **[CoreRemoting](API/CoreRemoting.md)** - Main remoting client and server classes, configuration, and core interfaces
  - `RemotingClient`, `RemotingServer`, `ClientConfig`, `ServerConfig`
  - `IRemotingClient`, `IRemotingServer`, `IServiceProxy`

#### Classic .NET Remoting Compatibility
- **[CoreRemoting.ClassicRemotingApi](API/CoreRemoting-ClassicRemotingApi.md)** - Classic .NET Remoting style APIs for migration
  - `RemotingConfiguration`, `WellKnownServiceTypeEntry`, `RemotingServices`
  - Configuration file support, service registration, and proxy utilities

### Infrastructure Namespaces

#### Transport Layer
- **[CoreRemoting.Channels](API/CoreRemoting-Channels.md)** - Network transport channels and communication protocols
  - TCP, WebSocket, Named Pipe, and Null channels
  - `IClientChannel`, `IServerChannel`, `IRawMessageTransport`

#### Security & Authentication
- **[CoreRemoting.Authentication](API/CoreRemoting-Authentication.md)** - Authentication framework and credential management
  - `IAuthenticationProvider`, `Credential`, `RemotingIdentity`
  - `AuthenticationRequestMessage`, `AuthenticationResponseMessage`

#### Dependency Injection
- **[CoreRemoting.DependencyInjection](API/CoreRemoting-DependencyInjection.md)** - DI container abstractions and implementations
  - `IDependencyInjectionContainer`, Microsoft DI and Castle Windsor adapters

#### Serialization
- **[CoreRemoting.Serialization](API/CoreRemoting-Serialization.md)** - Serialization framework and built-in serializers
  - `ISerializerAdapter`, BSON, NeoBinary, and custom serializer support

#### Messaging Framework
- **[CoreRemoting.RpcMessaging](API/CoreRemoting-RpcMessaging.md)** - RPC message types and message building framework
  - `MethodCallMessage`, `MethodCallResultMessage`, `IMessageEncryptionManager`

#### Remote Delegates & Events
- **[CoreRemoting.RemoteDelegates](API/CoreRemoting-RemoteDelegates.md)** - Remote delegate invocation and event handling
  - `IDelegateProxy`, `IDelegateProxyFactory`, event subscription across boundaries

#### Threading Utilities
- **[CoreRemoting.Threading](API/CoreRemoting-Threading.md)** - Async synchronization primitives and utilities
  - `AsyncLock`, `AsyncManualResetEvent`, `AsyncReaderWriterLock`

#### Utility Classes
- **[CoreRemoting.Toolbox](API/CoreRemoting-Toolbox.md)** - General utility classes and extensions
  - `TaskExtensions`, `LimitedSizeQueue<T>`

## Architecture Overview

CoreRemoting follows a layered architecture where each namespace provides specific functionality:

```
┌─────────────────────────────────────────────────────────────┐
│                    Application Layer                        │
│              (Your Services and Client Code)                │
├─────────────────────────────────────────────────────────────┤
│                      CoreRemoting                           │
│  RemotingClient, RemotingServer, Configuration, Proxies     │
├─────────────────────────────────────────────────────────────┤
│                   Framework Layer                           │
│  Authentication | DependencyInjection | Serialization      │
├─────────────────────────────────────────────────────────────┤
│                   Messaging Layer                           │
│            RpcMessaging | RemoteDelegates                  │
├─────────────────────────────────────────────────────────────┤
│                  Transport Layer                            │
│               Channels | Threading | Toolbox               │
└─────────────────────────────────────────────────────────────┘
```

## Key Extension Points

CoreRemoting is designed for extensibility. The main extension points are:

1. **Custom Channels** - Implement `IClientChannel`/`IServerChannel` for new transport protocols
2. **Custom Serializers** - Implement `ISerializerAdapter` for different data formats
3. **Custom Authentication** - Implement `IAuthenticationProvider` for auth mechanisms
4. **Custom DI Containers** - Implement `IDependencyInjectionContainer` for IoC frameworks
5. **Remote Events** - Use the remote delegates framework for event-driven communication

## Getting Started

If you're new to CoreRemoting, we recommend this reading order:

1. **[CoreRemoting](API/CoreRemoting.md)** - Understand the main API
2. **[Configuration](Configuration.md)** - Learn how to configure client and server
3. **[CoreRemoting.Channels](API/CoreRemoting-Channels.md)** - Choose appropriate transport channel
4. **[CoreRemoting.Serialization](API/CoreRemoting-Serialization.md)** - Select serialization format
5. **[CoreRemoting.Authentication](API/CoreRemoting-Authentication.md)** - Add security if needed

## Legend

- 🔄 **Interface** - Contract that must be implemented
- 🏗️ **Class** - Concrete implementation
- ⚙️ **Configuration** - Configuration class or related component
- 🔧 **Utility** - Helper or utility class
- 🚫 **Exception** - Exception class

---

*For conceptual documentation, tutorials, and getting started guides, see the main documentation sections.*
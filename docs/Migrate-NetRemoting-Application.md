# Migrate a .NET Remoting Application to CoreRemoting

This guide provides comprehensive instructions and patterns for migrating .NET Framework 4.8 Remoting applications to CoreRemoting, enabling cross-platform compatibility and modern .NET support.

## Table of Contents

1. [Overview](#overview)
2. [Migration Approaches](#migration-approaches)
3. [Step-by-Step Migration Guide](#step-by-step-migration-guide)
4. [Configuration Migration](#configuration-migration)
5. [Code Migration Patterns](#code-migration-patterns)
6. [Advanced Migration Scenarios](#advanced-migration-scenarios)
7. [Best Practices](#best-practices)
8. [Troubleshooting](#troubleshooting)

## Overview

### Why Migrate to CoreRemoting?

- **Cross-Platform Support**: Run on Windows, Linux, and macOS
- **Modern .NET Support**: Compatible with .NET Core 3.1+, .NET 5/6/7/8, and .NET Standard 2.0
- **Enhanced Security**: Built-in encryption and modern authentication providers
- **Multiple Transport Options**: TCP, WebSocket, NamedPipe, and QUIC channels
- **Flexible Serialization**: JSON-based (BSON), binary, and custom serializers
- **Dependency Injection Integration**: Native support for DI containers

### Migration Compatibility

CoreRemoting provides a **ClassicRemotingApi** compatibility layer that allows drop-in replacement for most .NET Remoting scenarios with minimal code changes.

## Migration Approaches

### 1. Classic Compatibility Layer (Recommended)

The ClassicRemotingApi provides a drop-in replacement with minimal code changes:

```csharp
// Before (.NET Remoting)
using System.Runtime.Remoting;
RemotingConfiguration.Configure(configFilePath, ensureSecurity: true);

// After (CoreRemoting)
using CoreRemoting.ClassicRemotingApi;
RemotingConfiguration.Configure(configFilePath);
```

### 2. Modern API Approach

For new implementations or comprehensive migrations:

```csharp
// Server-side
using var server = new RemotingServer(new ServerConfig()
{
    HostName = "localhost",
    NetworkPort = 9090,
    RegisterServicesAction = container =>
    {
        container.RegisterService<IService, ServiceImplementation>(ServiceLifetime.Singleton);
    }
});
server.Start();

// Client-side
using var client = new RemotingClient(new ClientConfig()
{
    ServerHostName = "localhost",
    ServerPort = 9090
});
client.Connect();
var proxy = client.CreateProxy<IService>();
```

## Step-by-Step Migration Guide

This guide walks through migrating a complete .NET Remoting application using the provided example.
You can find the original .NET Remoting example and the migrated example here:
[Migration Example Code](https://github.com/theRainbird/CoreRemoting/tree/master/Examples/MigrateNetRemoting)

### Example: Todo Service Migration

#### Original Structure (.NET Remoting)
```
TaskDemoAppNetRemoting/
├── TaskDemoAppNetRemoting.Server/
├── TaskDemoAppNetRemoting.Client/
└── TaskDemoAppNetRemoting.Shared/
```

#### Step 1: Create New Projects

1. **Create new solution** with modern project structure
2. **Target frameworks**: Use .NET Standard 2.0 for shared libraries, .NET 8.0 for applications
3. **Add CoreRemoting NuGet packages** to server and client projects

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CoreRemoting" Version="*" />
    <PackageReference Include="CoreRemoting.ClassicRemotingApi" Version="*" />
  </ItemGroup>
</Project>
```

#### Step 2: Update Configuration Files

**Original Server Config (App.config):**
```xml
<configuration>
  <system.runtime.remoting>
    <application name="TaskDemoAppNetRemoting">
      <service>
        <wellknown mode="Singleton"
          type="TaskDemoAppNetRemoting.Server.TodoService, TaskDemoAppNetRemoting.Server"
          objectUri="TodoService"/>
      </service>
      <channels>
        <channel ref="tcp" name="TaskDemoAppNetRemoting.Server" port="9090" secure="true"/>
      </channels>
    </application>
  </system.runtime.remoting>
</configuration>
```

**Migrated Server Config (App.config):**
```xml
<configuration>
  <configSections>
    <section name="coreRemoting" 
      type="CoreRemoting.ClassicRemotingApi.ConfigSection.CoreRemotingConfigSection, CoreRemoting"/>
  </configSections>
  
  <coreRemoting>
    <serverInstances>
      <add uniqueInstanceName="TaskServer" networkPort="8080" 
           serializer="binary" channel="ws"/>
    </serverInstances>
    <services>
      <add serviceName="TodoService" 
           interfaceAssemblyName="MigratedTaskDemoAppNetRemoting.Shared"
           interfaceTypeName="MigratedTaskDemoAppNetRemoting.Shared.ITodoService"
           implementationAssemblyName="MigratedTaskDemoAppNetRemoting.Server"
           implementationTypeName="MigratedTaskDemoAppNetRemoting.Server.TodoService" 
           lifetime="Singleton" 
           uniqueServerInstanceName="TaskServer"/>
    </services>
  </coreRemoting>
</configuration>
```

**Original Client Config (App.config):**
```xml
<configuration>
  <appSettings>
    <add key="serverUrl" value="tcp://localhost:9090" />
  </appSettings>
  <system.runtime.remoting>
    <application name="TaskDemoAppNetRemoting.Client">
      <channels>
        <channel ref="tcp" secure="true" />
      </channels>
    </application>
  </system.runtime.remoting>
</configuration>
```

**Migrated Client Config (App.config):**
```xml
<configuration>
  <configSections>
    <section name="coreRemoting" 
      type="CoreRemoting.ClassicRemotingApi.ConfigSection.CoreRemotingConfigSection, CoreRemoting"/>
  </configSections>
  
  <coreRemoting>
    <clientInstances>
      <add uniqueInstanceName="DefaultClient" 
           serverHostName="localhost" serverPort="8080" 
           serializer="binary" isDefault="true" channel="ws"/>
    </clientInstances>
  </coreRemoting>
</configuration>
```

#### Step 3: Update Server Code

**Original Server Program:**
```csharp
using System;
using System.Configuration;
using System.Runtime.Remoting;

namespace TaskDemoAppNetRemoting.Server
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            var configFilePath = ConfigurationManager
                .OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
            
            RemotingConfiguration.Configure(configFilePath, ensureSecurity: true);
            
            Console.WriteLine("Server running (Press [Enter] to quit)");
            Console.ReadLine();
        }
    }
}
```

**Migrated Server Program:**
```csharp
using System;
using System.Configuration;
using CoreRemoting.ClassicRemotingApi; // Changed namespace

namespace MigratedTaskDemoAppNetRemoting.Server
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            var configFilePath = ConfigurationManager
                .OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
            
            RemotingConfiguration.Configure(configFilePath); // Removed ensureSecurity parameter
            
            Console.WriteLine("Server running (Press [Enter] to quit)");
            Console.ReadLine();
        }
    }
}
```

#### Step 4: Update Client Proxy Creation

**Original Client ServiceProxyHelper:**
```csharp
using System;
using System.Configuration;
using TaskDemoAppNetRemoting.Shared;

namespace TaskDemoAppNetRemoting.Client
{
    public static class ServiceProxyHelper
    {
        private static string _serverUrl;

        public static string ServerUrl
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_serverUrl))
                    _serverUrl = ConfigurationManager.AppSettings.Get("serverUrl");
                return _serverUrl;
            }
        }

        public static ITodoService GetTaskServiceProxy()
        {
            return (ITodoService)Activator.GetObject(typeof(ITodoService), 
                ServerUrl + "/TodoService");
        }
    }
}
```

**Migrated Client ServiceProxyHelper:**
```csharp
using MigratedTaskDemoAppNetRemoting.Shared;
using CoreRemoting.ClassicRemotingApi; // Added namespace

namespace MigratedTaskDemoAppNetRemoting.Client
{
    public static class ServiceProxyHelper
    {
        public static ITodoService GetTaskServiceProxy()
        {
            // Create proxy using CoreRemoting.ClassicRemotingApi
            return (ITodoService)RemotingServices.Connect(
                interfaceType: typeof(ITodoService),
                serviceName: "TodoService");
            
            // Original code:
            // return (ITodoService)Activator.GetObject(typeof(ITodoService), 
            //     ServerUrl + "/TodoService");
        }
    }
}
```

#### Step 5: Verify Shared Interfaces

The service interfaces typically require minimal changes:

```csharp
using System;
using System.Collections.Generic;

namespace MigratedTaskDemoAppNetRemoting.Shared
{
    public interface ITodoService
    {
        List<Todo> GetTodoList();
        Todo SaveTodo(Todo item);
        void DeleteTodo(Guid id);
    }
}
```

**Note**: The Todo data class and service implementation usually require no changes.

## Configuration Migration

### Server Configuration Migration

| .NET Remoting Setting | CoreRemoting Equivalent |
|----------------------|------------------------|
| `<system.runtime.remoting>` | `<coreRemoting>` |
| `<wellknown mode="Singleton">` | `lifetime="Singleton"` |
| `objectUri="TodoService"` | `serviceName="TodoService"` |
| `<channel ref="tcp" port="9090">` | `networkPort="9090" channel="tcp"` |

### Client Configuration Migration

| .NET Remoting Setting | CoreRemoting Equivalent |
|----------------------|------------------------|
| `<appSettings>` server URL | `<clientInstances>` server configuration |
| `Activator.GetObject()` | `RemotingServices.Connect()` |
| Custom channel setup | Built-in channel selection |

### Serialization Options

- **Default**: BSON (JSON-based, widely compatible)
- **Binary**: Set `serializer="binary"` for binary serialization (**Depreated!** Needs additional CoreRemoting.Serialization.Binary nuget package)
- **NeoBinary**: Use `serializer="neobinary"` for modern binary format

### Channel Options

| Channel | Config Value | Use Case                                                                                            |
|---------|-------------|-----------------------------------------------------------------------------------------------------|
| TCP | `channel="tcp"` | Default, reliable TCP connection                                                                    |
| WebSocket | `channel="ws"` | Modern standard                                                                                     |                 |
| NamedPipe | `channel="namedpipe"` | Inter-process communication                                                                         |
| QUIC | `channel="quic"` | Modern protocol (.NET 9.0 only; Windows only! Needs additional CoreRemoting.Channels.Quic assembly) |

## Code Migration Patterns

### Pattern 1: Direct Compatibility Layer

For minimal changes, use the ClassicRemotingApi:

```csharp
// Replace
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;

// With
using CoreRemoting.ClassicRemotingApi;
```

### Pattern 2: Service Registration

**Before (App.config):**
```xml
<service>
  <wellknown mode="Singleton"
    type="MyNamespace.MyService, MyAssembly"
    objectUri="MyService"/>
</service>
```

**After (App.config):**
```xml
<services>
  <add serviceName="MyService"
       interfaceAssemblyName="MySharedAssembly"
       interfaceTypeName="MyNamespace.IMyService"
       implementationAssemblyName="MyServerAssembly"
       implementationTypeName="MyNamespace.MyService"
       lifetime="Singleton"/>
</services>
```

### Pattern 3: Client Proxy Creation

**Before:**
```csharp
var proxy = (IMyService)Activator.GetObject(
    typeof(IMyService), 
    "tcp://localhost:9090/MyService");
```

**After:**
```csharp
var proxy = (IMyService)RemotingServices.Connect(
    typeof(IMyService), 
    "MyService");
```

### Pattern 4: Modern API Migration

For full modernization, replace configuration with code:

**Server:**
```csharp
using var server = new RemotingServer(new ServerConfig()
{
    HostName = "localhost",
    NetworkPort = 9090,
    RegisterServicesAction = container =>
    {
        container.RegisterService<IMyService, MyService>(ServiceLifetime.Singleton);
    }
});
server.Start();
```

**Client:**
```csharp
using var client = new RemotingClient(new ClientConfig()
{
    ServerHostName = "localhost",
    ServerPort = 9090
});
client.Connect();
var proxy = client.CreateProxy<IMyService>();
```

## Advanced Migration Scenarios

### Event and Callback Migration

CoreRemoting supports events across the remoting boundary:

```csharp
// Service interface
public interface INotificationService
{
    event Action<string> NotificationReceived;
    void Subscribe();
    void Unsubscribe();
}

// Client usage
proxy.NotificationReceived += message => 
    Console.WriteLine($"Received: {message}");
```

### Authentication Migration

CoreRemoting provides enhanced authentication:

```xml
<coreRemoting>
  <serverInstances>
    <add uniqueInstanceName="SecureServer" 
         networkPort="9090"
         authenticationRequired="true"
         authenticationProvider="windows"/>
  </serverInstances>
</coreRemoting>
```

### Encryption Configuration

Enable message encryption:

```xml
<add uniqueInstanceName="SecureServer" 
     networkPort="9090"
     messageEncryption="true"/>
```

### Dependency Injection Integration

Modernize with DI containers:

```csharp
// Microsoft.Extensions.DependencyInjection
services.AddCoreRemotingServer(new ServerConfig
{
    RegisterServicesAction = container =>
    {
        container.RegisterService<IMyService, MyService>(ServiceLifetime.Singleton);
    }
});
```

## Best Practices

### 1. Incremental Migration

- **Phase 1**: Add CoreRemoting compatibility layer
- **Phase 2**: Update configuration files
- **Phase 3**: Test basic functionality
- **Phase 4**: Modernize API usage (optional)

### 2. Testing Strategy

1. **Unit Tests**: Test service implementations unchanged
2. **Integration Tests**: Verify client-server communication
3. **Performance Tests**: Compare with original .NET Remoting
4. **Cross-Platform Tests**: Test on multiple operating systems

### 3. Configuration Management

- **Environment-Specific Configs**: Use separate configs for development/staging/production
- **Security**: Enable authentication and encryption in production
- **Monitoring**: Add logging for troubleshooting

### 4. Error Handling

```csharp
try
{
    var proxy = RemotingServices.Connect<IMyService>("MyService");
    var result = proxy.GetData();
}
catch (CoreRemotingException ex)
{
    // Handle CoreRemoting-specific errors
}
catch (Exception ex)
{
    // Handle general exceptions
}
```

### 5. Resource Management

```csharp
// Use using statements for proper disposal
using var client = new RemotingClient(clientConfig);
client.Connect();

// Or manually dispose when needed
IServiceProxy proxy = null;
try
{
    proxy = client.CreateProxy<IMyService>();
    // Use proxy
}
finally
{
    proxy?.Dispose();
}
```

## Troubleshooting

### Common Issues

#### 1. Connection Timeouts

**Problem**: Client cannot connect to server
**Solution**: 
- Verify firewall settings
- Check port availability
- Ensure server is running before client

#### 2. Serialization Issues

**Problem**: Type not found during deserialization
**Solution**:
- Ensure shared assemblies are compatible
- Check serialization format consistency
- Verify type names are identical

#### 3. Authentication Failures

**Problem**: Authentication provider errors
**Solution**:
- Verify authentication provider configuration
- Check credentials format
- Ensure provider is available on target platform

#### 4. Channel Configuration

**Problem**: Channel not supported on platform
**Solution**:
- Use TCP for cross-platform compatibility
- QUIC requires .NET 9.0

### Debugging Tips

1. **Enable Logging**:
```xml
<coreRemoting>
  <logging enabled="true" level="Debug"/>
</coreRemoting>
```

2. **Verify Configuration**:
```csharp
// Validate configuration before starting
var config = new ServerConfig();
// ... configure
var server = new RemotingServer(config);
```

3. **Test Connectivity**:
```csharp
// Simple connectivity test
try
{
    client.Connect();
    Console.WriteLine("Connection successful");
}
catch (Exception ex)
{
    Console.WriteLine($"Connection failed: {ex.Message}");
}
```

## Conclusion

Migrating from .NET Remoting to CoreRemoting provides:

- **Cross-platform compatibility**
- **Enhanced security features**
- **Modern .NET support**
- **Flexible configuration options**
- **Multiple transport channels**

The ClassicRemotingApi compatibility layer enables incremental migration with minimal code changes, while the modern API provides full access to CoreRemoting's advanced features for comprehensive modernization.

For additional examples and patterns, refer to the provided migration examples in the `Examples/MigrateNetRemoting/` directory.
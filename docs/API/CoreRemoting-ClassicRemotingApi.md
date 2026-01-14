# CoreRemoting.ClassicRemotingApi Namespace API Reference

This namespace provides CoreRemoting configuration and registration APIs that follow the classic .NET Remoting patterns, making migration from traditional .NET Remoting to CoreRemoting easier.

## Core Classes

### 🏗️ RemotingConfiguration
**Namespace:** `CoreRemoting.ClassicRemotingApi`

Provides CoreRemoting configuration in classic .NET Remoting style. This static class serves as the central hub for registering, configuring, and managing CoreRemoting servers, clients, and services using familiar patterns from classic .NET Remoting.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `RegisteredServerInstances` | `IRemotingServer[]` | Gets a list of currently registered CoreRemoting server instances |
| `RegisteredClientInstances` | `IRemotingClient[]` | Gets a list of currently registered CoreRemoting client instances |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `RegisterServer(ServerConfig config)` | `string` | Registers a new CoreRemoting server instance and returns its unique instance name |
| `RegisterClient(ClientConfig config)` | `string` | Registers a new CoreRemoting client instance and returns its unique instance name |
| `GetRegisteredServer(string uniqueServerInstanceName)` | `IRemotingServer` | Gets a registered server instance by its unique name |
| `GetRegisteredClient(string uniqueClientInstanceName)` | `IRemotingClient` | Gets a registered client instance by its unique name |
| `UnregisterServer(string uniqueServerInstanceName)` | `void` | Unregisters a CoreRemoting server |
| `UnregisterServerAsync(string uniqueServerInstanceName)` | `Task` | Unregisters a CoreRemoting server asynchronously |
| `UnregisterClient(string uniqueClientInstanceName)` | `void` | Unregisters a CoreRemoting client |
| `UnregisterClientAsync(string uniqueClientInstanceName)` | `Task` | Unregisters a CoreRemoting client asynchronously |
| `RegisterWellKnownServiceType(WellKnownServiceTypeEntry entry)` | `void` | Registers a service using WellKnownServiceTypeEntry |
| `RegisterWellKnownServiceType(Type interfaceType, Type implementationType, ServiceLifetime lifetime, string serviceName = "", string uniqueServerInstanceName = "")` | `void` | Registers a service with explicit type parameters |
| `Configure(string fileName = "", Credential[] credentials = null)` | `void` | Applies CoreRemoting server configuration from config file |
| `ShutdownAll()` | `void` | Shutdown all registered clients and servers |
| `ShutdownAllAsync()` | `Task` | Shutdown all registered clients and servers asynchronously |

#### Usage Examples

```csharp
// Basic server registration
var serverConfig = new ServerConfig()
{
    HostName = "localhost",
    NetworkPort = 9090,
    RegisterServicesAction = container =>
    {
        container.RegisterService<ICalculatorService, CalculatorService>(ServiceLifetime.Singleton);
    }
};

var serverName = RemotingConfiguration.RegisterServer(serverConfig);
Console.WriteLine($"Server registered with name: {serverName}");

// Get registered server
var server = RemotingConfiguration.GetRegisteredServer(serverName);
```

```csharp
// Service registration using classic pattern
RemotingConfiguration.RegisterWellKnownServiceType(
    interfaceType: typeof(ICalculatorService),
    implementationType: typeof(CalculatorService),
    lifetime: ServiceLifetime.Singleton,
    serviceName: "CalculatorService");

// Alternative using WellKnownServiceTypeEntry
var serviceEntry = new WellKnownServiceTypeEntry(
    interfaceAssemblyName: "MyApp.Interfaces",
    interfaceTypeName: "MyApp.Interfaces.ICalculatorService",
    implementationAssemblyName: "MyApp.Services",
    implementationTypeName: "MyApp.Services.CalculatorService",
    lifetime: ServiceLifetime.Singleton,
    serviceName: "CalculatorService");

RemotingConfiguration.RegisterWellKnownServiceType(serviceEntry);
```

```csharp
// Configuration from XML file
RemotingConfiguration.Configure("app.config", new[]
{
    new Credential { Name = "username", Value = "admin" },
    new Credential { Name = "password", Value = "secret" }
});
```

```csharp
// Client registration
var clientConfig = new ClientConfig()
{
    ServerHostName = "localhost",
    ServerPort = 9090
};

var clientName = RemotingConfiguration.RegisterClient(clientConfig);
var client = RemotingConfiguration.GetRegisteredClient(clientName);
```

```csharp
// Cleanup
RemotingConfiguration.UnregisterServer(serverName);
RemotingConfiguration.UnregisterClient(clientName);

// Or shutdown everything
await RemotingConfiguration.ShutdownAllAsync();
```

---

### 🏗️ WellKnownServiceTypeEntry
**Namespace:** `CoreRemoting.ClassicRemotingApi`

Describes a well-known service registration entry, similar to classic .NET Remoting's WellKnownServiceTypeEntry. This class contains all metadata needed to register a service with CoreRemoting using assembly-qualified type names.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `InterfaceAssemblyName` | `string` | Interface assembly name of the service |
| `InterfaceTypeName` | `string` | Interface type name of the service |
| `ImplementationAssemblyName` | `string` | Implementation assembly name of the service |
| `ImplementationTypeName` | `string` | Implementation type name of the service |
| `Lifetime` | `ServiceLifetime` | Lifetime of the service (Singleton / SingleCall) |
| `ServiceName` | `string` | Unique service name (Full name of interface type is used, when left blank) |
| `UniqueServerInstanceName` | `string` | Unique instance name of the host server (default server is used, if left blank) |

#### Constructors

| Constructor | Description |
|-------------|-------------|
| `WellKnownServiceTypeEntry(string interfaceAssemblyName, string interfaceTypeName, string implementationAssemblyName, string implementationTypeName, ServiceLifetime lifetime, string serviceName = "", string uniqueServerInstanceName = "")` | Creates new service entry with all required parameters |

#### Usage Examples

```csharp
// Creating service entry manually
var serviceEntry = new WellKnownServiceTypeEntry(
    interfaceAssemblyName: "MyApp.Interfaces",
    interfaceTypeName: "MyApp.Interfaces.ICalculatorService",
    implementationAssemblyName: "MyApp.Services",
    implementationTypeName: "MyApp.Services.CalculatorService",
    lifetime: ServiceLifetime.Singleton,
    serviceName: "CalculatorService",
    uniqueServerInstanceName: "MainServer");

// Register the service
RemotingConfiguration.RegisterWellKnownServiceType(serviceEntry);
```

```csharp
// Dynamic service registration from configuration
var configServices = LoadServicesFromConfig();

foreach (var serviceConfig in configServices)
{
    var entry = new WellKnownServiceTypeEntry(
        serviceConfig.InterfaceAssembly,
        serviceConfig.InterfaceType,
        serviceConfig.ImplementationAssembly,
        serviceConfig.ImplementationType,
        serviceConfig.Lifetime,
        serviceConfig.ServiceName);
        
    RemotingConfiguration.RegisterWellKnownServiceType(entry);
}
```

---

### 🏗️ RemotingServices
**Namespace:** `CoreRemoting.ClassicRemotingApi`

Provides several methods for using and publishing remoted objects and proxies, similar to classic .NET Remoting's RemotingServices class. This class offers utility methods for proxy detection, one-way method checking, service registration, and proxy creation.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `IsOneWay(MethodBase method)` | `bool` | Returns whether the specified method is marked with OneWayAttribute |
| `IsTransparentProxy(object proxy)` | `bool` | Returns whether the given object is a transparent proxy or a real object |
| `Marshal(object serviceInstance, string serviceName, Type interfaceType, string uniqueServerInstanceName = "")` | `string` | Registers an object instance as CoreRemoting service |
| `Connect(Type interfaceType, string serviceName = "", string uniqueClientInstanceName = "")` | `object` | Creates a proxy for a remote CoreRemoting service |

#### Usage Examples

```csharp
// Check if method is one-way
MethodInfo methodInfo = typeof(IMyService).GetMethod("LogMessage");
bool isOneWay = RemotingServices.IsOneWay(methodInfo);

// Check if object is a proxy
object myObject = client.CreateProxy<IMyService>();
bool isProxy = RemotingServices.IsTransparentProxy(myObject);
```

```csharp
// Register an object instance as service
var myServiceInstance = new MyDataService();
var serviceName = RemotingServices.Marshal(
    serviceInstance: myServiceInstance,
    serviceName: "MyDataService",
    interfaceType: typeof(IDataService),
    uniqueServerInstanceName: "MainServer");

Console.WriteLine($"Service registered as: {serviceName}");
```

```csharp
// Create proxy for remote service using Connect
var calculatorProxy = RemotingServices.Connect(
    interfaceType: typeof(ICalculatorService),
    serviceName: "CalculatorService",
    uniqueClientInstanceName: "MainClient");

var calculator = (ICalculatorService)calculatorProxy;
int result = calculator.Add(5, 3);
```

```csharp
// Simple usage with default instances
var serviceProxy = RemotingServices.Connect(typeof(IMyService));
var service = (IMyService)serviceProxy;

service.DoWork();
```

---

## Migration from Classic .NET Remoting

### Classic .NET Remoting Pattern
```csharp
// Classic .NET Remoting
RemotingConfiguration.RegisterWellKnownServiceType(
    typeof(CalculatorService),
    "CalculatorService",
    WellKnownObjectMode.Singleton);

var calculator = (ICalculatorService)Activator.GetObject(
    typeof(ICalculatorService),
    "tcp://localhost:9090/CalculatorService");
```

### CoreRemoting Classic API Pattern
```csharp
// CoreRemoting with Classic API
RemotingConfiguration.RegisterWellKnownServiceType(
    interfaceType: typeof(ICalculatorService),
    implementationType: typeof(CalculatorService),
    lifetime: ServiceLifetime.Singleton,
    serviceName: "CalculatorService");

var client = new RemotingClient(new ClientConfig
{
    ServerHostName = "localhost",
    ServerPort = 9090
});

var calculator = client.CreateProxy<ICalculatorService>();

// Alternative: Use RemotingServices.Connect()
var calculatorProxy = RemotingServices.Connect(
    interfaceType: typeof(ICalculatorService),
    serviceName: "CalculatorService");

var calculator = (ICalculatorService)calculatorProxy;
```

---

## Configuration File Support

### XML Configuration Structure
```xml
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <configSections>
    <section name="coreRemoting" type="CoreRemoting.ClassicRemotingApi.ConfigSection.CoreRemotingConfigSection, CoreRemoting" />
  </configSections>
  
  <coreRemoting>
    <serverInstances>
      <add name="MainServer" hostName="localhost" networkPort="9090" />
    </serverInstances>
    
    <services>
      <add 
        interfaceAssembly="MyApp.Interfaces"
        interfaceType="MyApp.Interfaces.ICalculatorService"
        implementationAssembly="MyApp.Services"
        implementationType="MyApp.Services.CalculatorService"
        lifetime="Singleton"
        serviceName="CalculatorService" />
    </services>
    
    <clientInstances>
      <add name="MainClient" serverHostName="localhost" serverPort="9090" />
    </clientInstances>
  </coreRemoting>
</configuration>
```

### Loading Configuration
```csharp
// Load from default app.config
RemotingConfiguration.Configure();

// Load from custom configuration file
RemotingConfiguration.Configure("myapp.config", new[]
{
    new Credential { Name = "username", Value = "admin" },
    new Credential { Name = "password", Value = "secret" }
});
```

---

## Best Practices

### Service Registration
1. **Use Strong Typing**: Prefer the type-based overload over string-based registration when possible
2. **Consistent Naming**: Use meaningful service names that reflect their purpose
3. **Lifetime Selection**: Choose appropriate lifetimes (Singleton for stateless services, SingleCall for stateful)
4. **Assembly Management**: Ensure all referenced assemblies are available at runtime

### Instance Management
1. **Named Instances**: Use unique instance names when running multiple servers/clients
2. **Cleanup**: Always unregister instances when shutting down to prevent memory leaks
3. **Async Operations**: Prefer async methods for cleanup operations to avoid blocking

### Migration Strategy
1. **Gradual Migration**: Start with new services using CoreRemoting while keeping existing services
2. **Interface Compatibility**: Maintain interface compatibility during migration
3. **Testing**: Thoroughly test service behavior after migration
4. **Configuration**: Use XML configuration for complex setups to maintain separation of concerns

### Classic .NET Remoting Pattern
```csharp
// Classic .NET Remoting
RemotingConfiguration.RegisterWellKnownServiceType(
    typeof(CalculatorService),
    "CalculatorService",
    WellKnownObjectMode.Singleton);

RemotingConfiguration.RegisterActivatedServiceType(
    typeof(CalculatorService),
    "CalculatorService");

var calculator = (ICalculatorService)Activator.GetObject(
    typeof(ICalculatorService),
    "tcp://localhost:9090/CalculatorService");

var transparentProxy = RemotingServices.IsTransparentProxy(calculator);
var isOneWay = RemotingServices.IsOneWay(typeof(ICalculatorService).GetMethod("LogMessage"));
```

### CoreRemoting Classic API Pattern
```csharp
// CoreRemoting with Classic API
RemotingConfiguration.RegisterWellKnownServiceType(
    interfaceType: typeof(ICalculatorService),
    implementationType: typeof(CalculatorService),
    lifetime: ServiceLifetime.Singleton,
    serviceName: "CalculatorService");

var myService = new CalculatorService();
RemotingServices.Marshal(myService, "CalculatorService", typeof(ICalculatorService));

var calculator = RemotingServices.Connect(
    interfaceType: typeof(ICalculatorService),
    serviceName: "CalculatorService");

bool isProxy = RemotingServices.IsTransparentProxy(calculator);
bool isOneWay = RemotingServices.IsOneWay(typeof(ICalculatorService).GetMethod("LogMessage"));

int result = calculator.Add(5,3);
```

---

## Migration from Classic .NET Remoting
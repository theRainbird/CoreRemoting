# CoreRemoting.DependencyInjection Namespace API Reference

This namespace contains dependency injection container abstractions and implementations for CoreRemoting, allowing integration with various DI frameworks and providing service lifetime management.

## Core Interfaces

### 🔄 IDependencyInjectionContainer
**Namespace:** `CoreRemoting.DependencyInjection`  
**Interfaces:** `IDisposable`

Interface for dependency injection container implementations. Defines the contract for service registration, resolution, and lifetime management.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `GetService(string serviceName)` | `object` | Gets service instance by name |
| `GetService<TServiceInterface>(string serviceName = "")` | `TServiceInterface` | Gets service instance by type |
| `RegisterService<TServiceInterface, TServiceImpl>(ServiceLifetime lifetime, string serviceName = "", bool asHiddenSystemService = false)` | `void` | Registers service with implementation type |
| `RegisterService<TServiceInterface>(Func<TServiceInterface> factoryDelegate, ServiceLifetime lifetime, string serviceName = "", bool asHiddenSystemService = false)` | `void` | Registers service with factory |
| `GetServiceInterfaceType(string serviceName)` | `Type` | Gets interface type of a service |
| `GetAllRegisteredTypes()` | `IEnumerable<Type>` | Gets all registered types |
| `IsRegistered<TServiceInterface>(string serviceName = "")` | `bool` | Checks if service is registered |
| `GetServiceRegistrations(bool includeHiddenSystemServices = false)` | `IEnumerable<ServiceRegistration>` | Gets service registration information |
| `GetServiceRegistration(string serviceName)` | `ServiceRegistration` | Gets service registration by name |
| `CreateScope()` | `IDisposable` | Creates scope for scoped services |

#### Generic Type Constraints

- `TServiceInterface`: Must be a class (interface type)
- `TServiceImpl`: Must be a class implementing `TServiceInterface`

#### Usage Examples

```csharp
// Basic service registration
container.RegisterService<IUserService, UserService>(ServiceLifetime.Singleton);

// Named service registration
container.RegisterService<IDataService, SqlDataService>(
    ServiceLifetime.Singleton, 
    "SqlData");

// Factory-based registration
container.RegisterService<ILogger>(
    () => new ConsoleLogger(),
    ServiceLifetime.Singleton);

// Scoped service registration
container.RegisterService<IUnitOfWork, UnitOfWork>(ServiceLifetime.Scoped);

// Service resolution
var userService = container.GetService<IUserService>();
var sqlDataService = container.GetService<IDataService>("SqlData");
```

```csharp
// Checking service registration
if (container.IsRegistered<IUserService>())
{
    var service = container.GetService<IUserService>();
}

// Getting service information
var registrations = container.GetServiceRegistrations();
foreach (var registration in registrations)
{
    Console.WriteLine($"Service: {registration.ServiceName}, Lifetime: {registration.ServiceLifetime}");
}
```

**Implemented by:** `MicrosoftDependencyInjectionContainer`, `CastleWindsorDependencyInjectionContainer`

---

## Enumerations

### ⚙️ ServiceLifetime
**Namespace:** `CoreRemoting.DependencyInjection`

Describes the available service lifetime modes for registered services.

#### Values

| Value | Description |
|--------|-------------|
| `Singleton = 1` | One service instance serves all calls (shared instance) |
| `SingleCall = 2` | Every call is served by its own service instance (new instance per call) |
| `Scoped = 3` | One service instance per scope (new instance per scope) |

#### Usage Examples

```csharp
// Singleton - shared across all calls
container.RegisterService<IConfigurationService, ConfigurationService>(ServiceLifetime.Singleton);

// SingleCall - new instance for each method call
container.RegisterService<IRequestProcessor, RequestProcessor>(ServiceLifetime.SingleCall);

// Scoped - one instance per scope
container.RegisterService<IDbContext, AppDbContext>(ServiceLifetime.Scoped);
```

#### Lifetime Behavior

| Lifetime | Creation | Disposal | Thread Safety | Use Cases |
|----------|-----------|-----------|---------------|------------|
| **Singleton** | First call | Container disposal | Required | Configuration, logging, shared services |
| **SingleCall** | Every call | After each call | Not required | Stateless services, request processors |
| **Scoped** | Scope creation | Scope disposal | Required | Unit of work, per-request services |

---

## Data Classes

### 🏗️ ServiceRegistration
**Namespace:** `CoreRemoting.DependencyInjection`

Describes a registered service with its metadata, including interface type, implementation, lifetime, and factory information.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `ServiceName` | `string` | Unique name of the service |
| `InterfaceType` | `Type` | Interface type used as remote interface |
| `ImplementationType` | `Type` | Implementation type (null if factory is used) |
| `ServiceLifetime` | `ServiceLifetime` | Service lifetime model |
| `UsesFactory` | `bool` | Whether a factory is used for instance creation |
| `Factory` | `Delegate` | Factory delegate used for instance creation |
| `IsHiddenSystemService` | `bool` | Whether service is a hidden system service |
| `EventStub` | `EventStub` | Remote event handlers for the component |

#### Constructors

| Constructor | Description |
|-------------|-------------|
| `ServiceRegistration(string serviceName, Type interfaceType, Type implementationType, ServiceLifetime serviceLifetime, Delegate factory, bool isHiddenSystemService)` | Creates new service registration |

#### Usage Examples

```csharp
// Creating service registration manually
var registration = new ServiceRegistration(
    serviceName: "MyService",
    interfaceType: typeof(IMyService),
    implementationType: typeof(MyService),
    serviceLifetime: ServiceLifetime.Singleton,
    factory: null,
    isHiddenSystemService: false);

Console.WriteLine($"Service: {registration.ServiceName}");
Console.WriteLine($"Interface: {registration.InterfaceType.Name}");
Console.WriteLine($"Implementation: {registration.ImplementationType?.Name}");
Console.WriteLine($"Lifetime: {registration.ServiceLifetime}");
Console.WriteLine($"Uses Factory: {registration.UsesFactory}");
```

```csharp
// Enumerating service registrations
var registrations = container.GetServiceRegistrations();
foreach (var registration in registrations)
{
    if (!registration.IsHiddenSystemService)
    {
        Console.WriteLine($"Public Service: {registration.ServiceName}");
        Console.WriteLine($"  Interface: {registration.InterfaceType.FullName}");
        Console.WriteLine($"  Lifetime: {registration.ServiceLifetime}");
        
        if (registration.UsesFactory)
        {
            Console.WriteLine($"  Created by factory");
        }
        else
        {
            Console.WriteLine($"  Implementation: {registration.ImplementationType.FullName}");
        }
    }
}
```

---

## Container Implementations

### 🏗️ MicrosoftDependencyInjectionContainer
**Namespace:** `CoreRemoting.DependencyInjection`  
**Interfaces:** `IDependencyInjectionContainer`

Dependency injection container adapter for Microsoft.Extensions.DependencyInjection. Provides CoreRemoting service registration semantics using Microsoft's DI container.

#### Key Features

- **Full Microsoft DI Compatibility**: Works with existing Microsoft DI configurations
- **Scoped Support**: Proper scope management for scoped services
- **Factory Integration**: Supports factory-based service registration
- **Hidden System Services**: Marks CoreRemoting internal services appropriately

#### Usage Examples

```csharp
// Integration with Microsoft DI
var services = new ServiceCollection();

// Add regular Microsoft DI services
services.AddSingleton<ILogger, ConsoleLogger>();
services.AddScoped<IDbContext, AppDbContext>();

// Create CoreRemoting container adapter
var coreContainer = new MicrosoftDependencyInjectionContainer(services);

// Register CoreRemoting services
coreContainer.RegisterService<IUserService, UserService>(ServiceLifetime.Singleton);
coreContainer.RegisterService<IRequestHandler, RequestHandler>(ServiceLifetime.SingleCall);

// Use with CoreRemoting server
var config = new ServerConfig()
{
    DependencyInjectionContainer = coreContainer,
    RegisterServicesAction = container =>
    {
        container.RegisterService<ICalculator, Calculator>(ServiceLifetime.Singleton);
    }
};
```

```csharp
// Advanced factory registration
services.AddSingleton<IMyService>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var logger = provider.GetRequiredService<ILogger>();
    return new MyService(config, logger);
});
```

---

### 🏗️ CastleWindsorDependencyInjectionContainer
**Namespace:** `CoreRemoting.DependencyInjection`  
**Interfaces:** `IDependencyInjectionContainer`

Dependency injection container adapter for Castle Windsor. Provides CoreRemoting service registration semantics using Castle Windsor IoC container.

#### Key Features

- **Castle Windsor Integration**: Full compatibility with Castle Windsor features
- **Advanced Lifestyle Support**: Singleton, Transient, Scoped, etc.
- **Interceptor Support**: Built-in support for Castle interceptors
- **Configuration Flexibility**: XML, code-based, and attribute configuration

#### Usage Examples

```csharp
// Create Castle Windsor container
var castleContainer = new WindsorContainer();

// Create CoreRemoting adapter
var coreContainer = new CastleWindsorDependencyInjectionContainer(castleContainer);

// Register services
coreContainer.RegisterService<IUserService, UserService>(ServiceLifetime.Singleton);
coreContainer.RegisterService<IRequestProcessor, RequestProcessor>(ServiceLifetime.SingleCall);

// Advanced Castle Windsor features
castleContainer.Register(
    Component.For<IDbContext>()
        .ImplementedBy<AppDbContext>()
        .LifestylePerWebRequest()
        .DependsOn(
            Dependency.OnValue("connectionString", ConfigurationManager.ConnectionStrings["Default"].ConnectionString)));

// Use with CoreRemoting server
var config = new ServerConfig()
{
    DependencyInjectionContainer = coreContainer
};
```

---

### 🏗️ DependencyInjectionContainerBase
**Namespace:** `CoreRemoting.DependencyInjection`

Abstract base class for dependency injection container implementations. Provides common functionality and template methods for container adapters.

#### Key Features

- **Registration Management**: Common service registration logic
- **Lifetime Mapping**: Standardized lifetime management
- **Validation**: Input validation and error handling
- **Event Handling**: Integration with CoreRemoting event system

#### Usage Examples

```csharp
// Creating custom container adapter
public class MyCustomContainer : DependencyInjectionContainerBase
{
    private readonly IMyContainer _container;
    
    public MyCustomContainer(IMyContainer container)
    {
        _container = container;
    }
    
    public override void RegisterService<TServiceInterface, TServiceImpl>(
        ServiceLifetime lifetime, 
        string serviceName = "", 
        bool asHiddenSystemService = false)
    {
        // Map CoreRemoting lifetime to custom container lifetime
        var customLifetime = MapToCustomLifetime(lifetime);
        
        _container.Register<TServiceInterface, TServiceImpl>(customLifetime, serviceName);
        
        // Store registration information
        StoreRegistration(serviceName, typeof(TServiceInterface), typeof(TServiceImpl), lifetime);
    }
    
    public override TServiceInterface GetService<TServiceInterface>(string serviceName = "")
    {
        return _container.Resolve<TServiceInterface>(serviceName);
    }
    
    // Implement other abstract methods...
}
```

---

## Extension Methods

### 🔧 DependencyInjectionContainerExtensions
**Namespace:** `CoreRemoting.DependencyInjection`

Extension methods for dependency injection containers providing additional convenience methods and utilities.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `RegisterTransient<TInterface, TImplementation>(this IDependencyInjectionContainer container, string serviceName = "")` | `void` | Registers transient service (equivalent to SingleCall) |
| `RegisterSingleton<TInterface, TImplementation>(this IDependencyInjectionContainer container, string serviceName = "")` | `void` | Registers singleton service |
| `RegisterScoped<TInterface, TImplementation>(this IDependencyInjectionContainer container, string serviceName = "")` | `void` | Registers scoped service |
| `TryGetService<TInterface>(this IDependencyInjectionContainer container, string serviceName = "")` | `TInterface` | Tries to get service, returns null if not registered |
| `GetRequiredService<TInterface>(this IDependencyInjectionContainer container, string serviceName = "")` | `TInterface` | Gets service, throws if not registered |

#### Usage Examples

```csharp
// Using extension methods for common registration patterns
container.RegisterTransient<IUserService, UserService>();
container.RegisterSingleton<IConfigurationService, ConfigurationService>();
container.RegisterScoped<IDbContext, AppDbContext>("MainDbContext");

// Safe service resolution
var service = container.TryGetService<IOptionalService>();
if (service != null)
{
    // Service is available
}

// Required service resolution
var requiredService = container.GetRequiredService<IRequiredService>();
```

---

## Service Registration Patterns

### Factory-Based Registration

```csharp
// Simple factory
container.RegisterService<ILogger>(
    () => new ConsoleLogger(),
    ServiceLifetime.Singleton);

// Factory with dependencies
container.RegisterService<IDataService>(
    () => new DataService(
        container.GetService<IConfiguration>(),
        container.GetService<ILogger>()),
    ServiceLifetime.Singleton);

// Factory with complex initialization
container.RegisterService<IDatabase>(
    () =>
    {
        var config = container.GetService<IConfiguration>();
        var db = new Database(config.ConnectionString);
        db.Initialize();
        return db;
    },
    ServiceLifetime.Singleton);
```

### Conditional Registration

```csharp
// Register based on configuration
if (ConfigurationManager.AppSettings["UseCache"] == "true")
{
    container.RegisterService<ICacheService, RedisCacheService>(ServiceLifetime.Singleton);
}
else
{
    container.RegisterService<ICacheService, MemoryCacheService>(ServiceLifetime.Singleton);
}
```

### Named Service Registration

```csharp
// Register multiple implementations of same interface
container.RegisterService<IDataProvider, SqlDataProvider>(ServiceLifetime.Singleton, "Sql");
container.RegisterService<IDataProvider, XmlDataProvider>(ServiceLifetime.Singleton, "Xml");
container.RegisterService<IDataProvider, ApiDataProvider>(ServiceLifetime.Singleton, "Api");

// Resolve specific implementation
var sqlProvider = container.GetService<IDataProvider>("Sql");
var xmlProvider = container.GetService<IDataProvider>("Xml");
```

---

## Scope Management

### Using Scoped Services

```csharp
// Creating a scope
using (var scope = container.CreateScope())
{
    // All scoped services resolve to same instance within scope
    var context1 = container.GetService<IDbContext>();
    var context2 = container.GetService<IDbContext>();
    
    // context1 and context2 are the same instance
    
    // Singleton services still resolve to same instance across scopes
    var logger1 = container.GetService<ILogger>();
    var logger2 = container.GetService<ILogger>();
    
    // logger1 and logger2 are the same instance
}

// Scope ends, scoped services are disposed
```
---

## Best Practices

### Service Design Guidelines

1. **Prefer Interfaces**: Always register interfaces, not concrete types
2. **Single Responsibility**: Each service should have one clear responsibility
3. **Constructor Injection**: Use constructor injection, not service locator pattern
4. **Avoid Circular Dependencies**: Design services to avoid circular references
5. **Appropriate Lifetime**: Choose correct lifetime for each service type

### Lifetime Selection

| Service Type | Recommended Lifetime | Example |
|---------------|---------------------|----------|
| **Configuration** | Singleton | `IConfigurationService`, `IAppSettings` |
| **Stateless Services** | Singleton or SingleCall | `IValidator`, `ITransformer` |
| **Stateful Services** | Scoped | `IUnitOfWork`, `IRequestContext` |
| **Heavy Resources** | Singleton | `IDatabaseConnection`, `ICacheProvider` |
| **Request-Specific** | SingleCall | `ICommandHandler`, `IQueryProcessor` |

### Error Handling

```csharp
// Safe service resolution with error handling
public TService GetServiceSafely<TService>(string serviceName = "") where TService : class
{
    try
    {
        return container.GetService<TService>(serviceName);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, $"Failed to resolve service {typeof(TService).Name}");
        return null;
    }
}

// Validation before resolution
if (!container.IsRegistered<IMyService>())
{
    throw new InvalidOperationException("Required service not registered");
}

var service = container.GetService<IMyService>();
```

---

## See Also

- [CoreRemoting](CoreRemoting.md) - Core client and server classes
- [CoreRemoting.RemoteDelegates](CoreRemoting-RemoteDelegates.md) - Event handling and delegates
- [CoreRemoting.RpcMessaging](CoreRemoting-RpcMessaging.md) - Message framework
- [Configuration](../Configuration.md) - Container configuration options
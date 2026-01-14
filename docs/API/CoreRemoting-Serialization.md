# CoreRemoting.Serialization Namespace API Reference

This namespace contains the serialization framework for CoreRemoting, providing multiple serialization adapters and utilities for cross-framework object serialization.

## Core Interfaces

### 🔄 ISerializerAdapter
**Namespace:** `CoreRemoting.Serialization`

Interface that serializer adapter components must implement. Defines the contract for object serialization and deserialization.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `EnvelopeNeededForParameterSerialization` | `bool` | Whether parameter values must be put in an envelope object for proper deserialization |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Serialize<T>(T graph)` | `byte[]` | Serializes an object graph of type T |
| `Serialize(Type type, object graph)` | `byte[]` | Serializes an object graph with specified type |
| `Deserialize<T>(byte[] rawData)` | `T` | Deserializes raw data back to object of type T |
| `Deserialize(Type type, byte[] rawData)` | `object` | Deserializes raw data back to object of specified type |

#### Usage Examples

```csharp
// Creating custom serializer adapter
public class MyCustomSerializer : ISerializerAdapter
{
    public bool EnvelopeNeededForParameterSerialization => false;
    
    public byte[] Serialize<T>(T graph)
    {
        // Custom serialization logic
        return MyCustomSerialization.Serialize(graph);
    }
    
    public byte[] Serialize(Type type, object graph)
    {
        // Custom serialization logic with type information
        return MyCustomSerialization.Serialize(type, graph);
    }
    
    public T Deserialize<T>(byte[] rawData)
    {
        // Custom deserialization logic
        return MyCustomSerialization.Deserialize<T>(rawData);
    }
    
    public object Deserialize(Type type, byte[] rawData)
    {
        // Custom deserialization logic with type information
        return MyCustomSerialization.Deserialize(type, rawData);
    }
}
```

**Implemented by:** `BsonSerializerAdapter`, `NeoBinarySerializerAdapter`

---

## Built-in Serializer Implementations

### 🏗️ BsonSerializerAdapter
**Namespace:** `CoreRemoting.Serialization.Bson`  
**Interfaces:** `ISerializerAdapter`

Serializer adapter for BSON (Binary JSON) serialization using Newtonsoft.Json. Provides robust cross-platform serialization with excellent type support.

#### Key Features

- **Type Safety**: Full type name preservation for polymorphic objects
- **Performance**: Binary format for efficient network transmission
- **Compatibility**: Works across different .NET platforms
- **Extensible**: Custom JSON converters support
- **Reference Handling**: Circular reference and object reference preservation

#### Configuration

```csharp
var config = new BsonSerializerConfig()
{
    AddCommonJsonConverters = true,
    JsonConverters = new List<JsonConverter>
    {
        new MyCustomConverter(),
        new DateTimeOffsetConverter()
    }
};

var serializer = new BsonSerializerAdapter(config);
```

#### Usage Examples

```csharp
// Default configuration
var serializer = new BsonSerializerAdapter();

// Client configuration
var clientConfig = new ClientConfig()
{
    Serializer = new BsonSerializerAdapter(),
    // ... other settings
};

// Server configuration
var serverConfig = new ServerConfig()
{
    Serializer = new BsonSerializerAdapter(
        new BsonSerializerConfig()
        {
            AddCommonJsonConverters = true
        }),
    // ... other settings
};
```
---

### 🏗️ NeoBinarySerializerAdapter
**Namespace:** `CoreRemoting.Serialization.NeoBinary`  
**Interfaces:** `ISerializerAdapter`

High-performance binary serializer adapter using IL generation for maximum speed. Optimized for CoreRemoting scenarios.

#### Key Features

- **Maximum Performance**: IL-based serialization for minimal overhead
- **Type Safety**: Strong typing with validation
- **Caching**: Automatic serialization method caching
- **Security**: Type validation and safe deserialization
- **Zero Allocation**: Optimized for minimal memory allocation

#### Configuration

```csharp
var config = new NeoBinarySerializerConfig()
{
    IncludeAssemblyVersions = false,
    UseTypeReferences = true,
    MaxObjectGraphDepth = 100,
    MaxSerializedSize = 100 * 1024 * 1024, // 100MB
    AllowUnknownTypes = true,
    UseIlCompactLayout = true,
    EnableCompression = false,
    IncludeFieldNames = true,
    AllowExpressions = false,
    AllowReflectionTypes = true,
    EnableBinaryDataSetSerialization = false
};

var serializer = new NeoBinarySerializerAdapter(config);
```

#### Usage Examples

```csharp
var serializer = new NeoBinarySerializerAdapter();

var clientConfig = new ClientConfig()
{
    Serializer = new NeoBinarySerializerAdapter(),
    // ... other settings
};
```

## Data Classes

### 🏗️ ServiceReference
**Namespace:** `CoreRemoting.Serialization`  
**Attributes:** `[Serializable]`

Describes a reference to a remote service that can be passed across remoting boundaries.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `ServiceInterfaceTypeName` | `string` | Interface type name of the referenced service (e.g., "MyNamespace.IMyService, MyAssembly") |
| `ServiceName` | `string` | Name of the referenced service |

#### Constructors

| Constructor | Description |
|-------------|-------------|
| `ServiceReference(string serviceInterfaceTypeName, string serviceName)` | Creates new service reference |

---

### 🏗️ SerializableException
**Namespace:** `CoreRemoting.Serialization`  
**Attributes:** `[Serializable]`

Serializable exception replacement for non-serializable exceptions. Ensures all exceptions can be transmitted across remoting boundaries.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `SourceTypeName` | `string` | Type name of the original exception |
| `StackTrace` | `string` | Stack trace of the original exception |

#### Constructors

| Constructor | Description |
|-------------|-------------|
| `SerializableException(string typeName, string message)` | Creates with type name and message |
| `SerializableException(string typeName, string message, Exception innerException)` | Creates with type name, message, and inner exception |
| `SerializableException(string typeName, string message, string newStackTrace)` | Creates with type name, message, and custom stack trace |

#### Usage Examples

```csharp
// Automatically created by CoreRemoting when exception is not serializable
try
{
    // Some operation that throws non-serializable exception
}
catch (Exception ex)
{
    var serializable = ex.ToSerializable();
    // serializable can be transmitted across remoting boundary
}
```

---

## Utility Classes

### 🔧 CrossFrameworkSerialization
**Namespace:** `CoreRemoting.Serialization`

Provides tools to support serialization between different .NET frameworks (i.e. .NET 6.x and .NET Framework 4.x) by handling assembly redirection.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `RedirectAssembly(string assemblyShortName, string replacementAssemblyShortName)` | `void` | Redirects all loading attempts from a specified assembly name to another assembly name |
| `RedirectPrivateCoreLibToMscorlib()` | `void` | Redirects assembly "System.Private.CoreLib" to "mscorlib" |
| `RedirectMscorlibToPrivateCoreLib()` | `void` | Redirects assembly "mscorlib" to "System.Private.CoreLib" |

#### Usage Examples

```csharp
// Redirect System.Private.CoreLib to mscorlib for .NET Framework compatibility
CrossFrameworkSerialization.RedirectPrivateCoreLibToMscorlib();

// Custom assembly redirection for cross-framework compatibility
CrossFrameworkSerialization.RedirectAssembly("MyApp.Core", "MyApp.Core.Legacy");

// Redirect mscorlib to System.Private.CoreLib for modern .NET compatibility
CrossFrameworkSerialization.RedirectMscorlibToPrivateCoreLib();
```

---

### 🔧 ExceptionExtensions
**Namespace:** `CoreRemoting.Serialization`

Extension methods for exception handling and serialization.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `ToSerializable(this Exception exception)` | `SerializableException` | Converts exception to serializable version |
| `ToSerializable(this Exception exception, bool preserveStackTrace)` | `SerializableException` | Converts with stack trace control |

#### Usage Examples

```csharp
try
{
    // Some operation
}
catch (Exception ex)
{
    var serializableEx = ex.ToSerializable();
    // Can now be transmitted across remoting boundary
}

// Preserve original stack trace
var serializableEx = originalException.ToSerializable(preserveStackTrace: true);
```

---

## Configuration Classes

### ⚙️ BsonSerializerConfig
**Namespace:** `CoreRemoting.Serialization.Bson`

Configuration for BSON serializer adapter.

#### Key Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AddCommonJsonConverters` | `bool` | true | Whether to include common JSON converters |
| `JsonConverters` | `List<JsonConverter>` | null | Custom JSON converters to include |

#### Usage Examples

```csharp
var config = new BsonSerializerConfig()
{
    AddCommonJsonConverters = true,
    JsonConverters = new List<JsonConverter>
    {
        new MyCustomConverter(),
        new SpecialEnumConverter()
    }
};
```

---

### ⚙️ NeoBinarySerializerConfig
**Namespace:** `CoreRemoting.Serialization.NeoBinary`

Configuration for NeoBinary serializer adapter.

#### Key Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IncludeAssemblyVersions` | `bool` | false | Whether assembly versions should be included in type metadata |
| `UseTypeReferences` | `bool` | true | Whether type references should be used for circular reference handling |
| `MaxObjectGraphDepth` | `int` | 100 | Maximum depth of object graph to prevent stack overflow |
| `MaxSerializedSize` | `long` | 104857600 | Maximum allowed serialized data size in bytes (100MB) |
| `AllowedTypes` | `HashSet<Type>` | empty | Set of explicitly allowed types for deserialization |
| `BlockedTypes` | `HashSet<Type>` | empty | Set of explicitly blocked types for deserialization |
| `AllowUnknownTypes` | `bool` | true | Whether unknown types should be allowed during deserialization |
| `AllowExpressions` | `bool` | false | Whether LINQ expressions should be allowed during serialization/deserialization |
| `AllowReflectionTypes` | `bool` | true | Whether reflection types (MethodInfo, PropertyInfo, etc.) should be allowed |
| `IncludeFieldNames` | `bool` | true | Whether to include field names during serialization |
| `UseIlCompactLayout` | `bool` | true | Enables IL compact layout for complex objects (better performance) |
| `EnableCompression` | `bool` | false | Whether to compress serialized data |
| `CompressionLevel` | `CompressionLevel` | Optimal | Compression level when compression is enabled |
| `EnableBinaryDataSetSerialization` | `bool` | false | Whether to use binary serialization for DataSets/DataTables |

#### Usage Examples

```csharp
var config = new NeoBinarySerializerConfig()
{
    IncludeAssemblyVersions = false,
    UseTypeReferences = true,
    MaxObjectGraphDepth = 100,
    MaxSerializedSize = 50 * 1024 * 1024, // 50MB
    AllowUnknownTypes = false, // More secure
    UseIlCompactLayout = true, // Better performance
    EnableCompression = true,
    IncludeFieldNames = true,
    AllowExpressions = false,
    AllowReflectionTypes = false, // More secure
    EnableBinaryDataSetSerialization = true
};
```
---

## See Also

- [CoreRemoting](CoreRemoting.md) - Core client and server classes
- [CoreRemoting.RpcMessaging](CoreRemoting-RpcMessaging.md) - Message types and framework
- [CoreRemoting.Channels](CoreRemoting-Channels.md) - Transport layer
- [Serialization](../Serialization.md) - Serialization concepts and configuration
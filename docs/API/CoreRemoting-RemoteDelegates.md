# CoreRemoting.RemoteDelegates Namespace API Reference

This namespace contains the remote delegates and events framework for CoreRemoting, enabling event-driven communication across remoting boundaries.

## Core Interfaces

### 🔄 IDelegateProxy
**Namespace:** `CoreRemoting.RemoteDelegates`

Interface for delegate proxy implementations that handle remote delegate invocation.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `HandlerKey` | `string` | Unique key identifying the delegate handler |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `InvokeDelegate(object[] args)` | `object` | Invokes the delegate with specified arguments |

**Implemented by:** `DelegateProxy`

---

### 🔄 IDelegateProxyFactory
**Namespace:** `CoreRemoting.RemoteDelegates`

Interface for delegate proxy factories that create and manage remote delegate proxies.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `CreateDelegateProxy<TDelegate>()` | `IDelegateProxy` | Creates delegate proxy for specified delegate type |
| `CreateDelegateProxy(Delegate targetDelegate)` | `IDelegateProxy` | Creates delegate proxy from existing delegate |


**Implemented by:** `DelegateProxyFactory`

---

### 🔄 IDelegateInvoker
**Namespace:** `CoreRemoting.RemoteDelegates`

Interface for delegate invokers that can invoke delegates dynamically and safely.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Invoke(Delegate targetDelegate, object[] args)` | `object` | Invokes delegate with arguments |
| `InvokeSafe(Delegate targetDelegate, object[] args)` | `object` | Safely invokes delegate with exception handling |

#### Usage Examples

```csharp
// Safe delegate invocation
var invoker = new SafeDynamicInvoker();
var result = invoker.InvokeSafe(myDelegate, new object[] { param1, param2 });
```

**Implemented by:** `SafeDynamicInvoker`, `SimpleDynamicInvoker`

---

## Core Classes

### 🏗️ DelegateProxy
**Namespace:** `CoreRemoting.RemoteDelegates`  
**Interfaces:** `IDelegateProxy`

Implementation of delegate proxy that handles remote delegate invocation across remoting boundaries.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `HandlerKey` | `string` | Unique identifier for the delegate handler |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `InvokeDelegate(object[] args)` | `object` | Invokes the remote delegate with specified arguments |

---

### 🏗️ DelegateProxyFactory
**Namespace:** `CoreRemoting.RemoteDelegates`  
**Interfaces:** `IDelegateProxyFactory`

Factory for creating delegate proxies that can be used for remote event subscription and delegate invocation.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `CreateDelegateProxy<TDelegate>()` | `IDelegateProxy` | Creates delegate proxy for generic delegate type |
| `CreateDelegateProxy(Delegate targetDelegate)` | `IDelegateProxy` | Creates delegate proxy from existing delegate |

---

### 🏗️ EventStub
**Namespace:** `CoreRemoting.RemoteDelegates`

Utility class that handles event subscription and invocation across remoting boundaries.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `DelegateInvoker` | `IDelegateInvoker` | Invoker used to call delegates safely |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `AddEventHandler(string eventName, Delegate handler)` | `void` | Adds event handler for remote event |
| `RemoveEventHandler(string eventName, Delegate handler)` | `void` | Removes event handler for remote event |
| `InvokeEvent(string eventName, object[] args)` | `void` | Invokes event with specified arguments |

---

### 🏗️ SafeDynamicInvoker
**Namespace:** `CoreRemoting.RemoteDelegates`  
**Interfaces:** `IDelegateInvoker`

Safe delegate invoker that provides exception handling and type safety for dynamic delegate invocation.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Invoke(Delegate targetDelegate, object[] args)` | `object` | Invokes delegate with arguments |
| `InvokeSafe(Delegate targetDelegate, object[] args)` | `object` | Safely invokes delegate with exception handling |

#### Usage Examples

```csharp
// Safe invocation
var invoker = new SafeDynamicInvoker();

try
{
    var result = invoker.InvokeSafe(myDelegate, args);
    // Handle result
}
catch (Exception ex)
{
    // Exception from delegate invocation
    Console.WriteLine($"Delegate failed: {ex.Message}");
}
```

---

### 🏗️ SimpleDynamicInvoker
**Namespace:** `CoreRemoting.RemoteDelegates`  
**Interfaces:** `IDelegateInvoker`

Simple delegate invoker that provides basic dynamic delegate invocation without extensive error handling.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Invoke(Delegate targetDelegate, object[] args)` | `object` | Invokes delegate with arguments |
| `InvokeSafe(Delegate targetDelegate, object[] args)` | `object` | Safely invokes delegate with minimal exception handling |

#### Usage Examples

```csharp
// Simple fast invocation
var invoker = new SimpleDynamicInvoker();
var result = invoker.Invoke(myDelegate, args);
```

---

## Data Classes

### 🏗️ ClientDelegateInfo
**Namespace:** `CoreRemoting.RemoteDelegates`

Information about a client-side delegate registered for remote invocation.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `Delegate` | `Delegate` | The actual delegate object |
| `HandlerKey` | `string` | Unique key identifying the delegate handler |
| `DelegateType` | `Type` | Type of the delegate |

---

### 🏗️ ClientDelegateRegistry
**Namespace:** `CoreRemoting.RemoteDelegates`

Registry for managing client-side delegates that can be invoked remotely from the server.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `RegisterDelegate(Delegate targetDelegate)` | `string` | Registers delegate and returns handler key |
| `GetDelegateByHandlerKey(string handlerKey)` | `Delegate` | Gets delegate by handler key |
| `UnregisterDelegate(string handlerKey)` | `bool` | Unregisters delegate by handler key |
| `Clear()` | `void` | Clears all registered delegates |

---

### 🏗️ RemoteDelegateInfo
**Namespace:** `CoreRemoting.RemoteDelegates`

Information about a remote delegate for tracking and management purposes.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `HandlerKey` | `string` | Unique key for the delegate handler |
| `DelegateType` | `Type` | Type information for the delegate |
| `MethodName` | `string` | Name of the method the delegate represents |

---

### 🏗️ RemoteDelegateInvocationEventAggregator
**Namespace:** `CoreRemoting.RemoteDelegates`

Aggregates and manages remote delegate invocations, providing efficient event distribution.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `RegisterHandler(string handlerKey, Delegate handler)` | `void` | Registers handler for remote delegate |
| `UnregisterHandler(string handlerKey)` | `void` | Unregisters handler |
| `InvokeHandler(string handlerKey, object[] args)` | `object` | Invokes handler with arguments |

---

## Remote Events Usage Examples

### Server-Side Event Definition

```csharp
public interface IStockService
{
    event EventHandler<StockUpdateEventArgs> StockUpdated;
    event Action<string> StockAlert;
    void UpdateStock(string symbol, decimal price);
}

public class StockService : IStockService
{
    public event EventHandler<StockUpdateEventArgs> StockUpdated;
    public event Action<string> StockAlert;
    
    public void UpdateStock(string symbol, decimal price)
    {
        // Update stock data
        
        // Raise events - these will be sent to subscribed clients
        StockUpdated?.Invoke(this, new StockUpdateEventArgs(symbol, price));
        
        if (price > 100m)
        {
            StockAlert?.Invoke($"High price alert: {symbol} at {price}");
        }
    }
}
```

### Client-Side Event Subscription

```csharp
// Create proxy to remote service
var stockService = client.CreateProxy<IStockService>();

// Subscribe to events
stockService.StockUpdated += (sender, args) =>
{
    Console.WriteLine($"Stock updated: {args.Symbol} = {args.Price}");
};

stockService.StockAlert += (message) =>
{
    Console.WriteLine($"ALERT: {message}");
};

// Call service method that triggers events
stockService.UpdateStock("AAPL", 150.25m);
```

### Bidirectional Delegates

```csharp
// Service interface with callback
public interface IProcessingService
{
    event Action<ProcessingResult> ProcessingCompleted;
    void StartProcessing(string data, Action<int> progressCallback);
}

// Client usage
var processingService = client.CreateProxy<IProcessingService>();

// Subscribe to completion event
processingService.ProcessingCompleted += (result) =>
{
    Console.WriteLine($"Processing completed: {result.Status}");
};

// Call with progress callback
processingService.StartProcessing("large data", (progress) =>
{
    Console.WriteLine($"Progress: {progress}%");
});
```

---

## Best Practices

### Event Design

1. **Event Arguments**: Use custom event args classes for complex data
2. **Exception Handling**: Handle exceptions in event handlers gracefully
3. **Memory Management**: Unsubscribe from events when no longer needed
4. **Thread Safety**: Consider thread safety for event handlers

### Performance Considerations

1. **Event Frequency**: Be cautious with high-frequency events
2. **Data Size**: Keep event arguments small and serializable
3. **Batch Updates**: Aggregate frequent small updates into batches
4. **Subscription Management**: Limit the number of event subscriptions

### Error Handling

```csharp
// Safe event handling
myService.SomeEvent += (sender, args) =>
{
    try
    {
        // Handle event
        ProcessEventData(args);
    }
    catch (Exception ex)
    {
        // Log error but don't throw back to server
        logger.LogError(ex, "Event handler failed");
    }
};
```

---

## See Also

- [CoreRemoting](CoreRemoting.md) - Core client and server classes
- [CoreRemoting.RpcMessaging](CoreRemoting-RpcMessaging.md) - Message framework
- [CoreRemoting.DependencyInjection](CoreRemoting-DependencyInjection.md) - Service registration
- [Events and Callbacks](../Events-and-Callbacks.md) - Event concepts
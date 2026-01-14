# CoreRemoting.Toolbox Namespace API Reference

This namespace contains utility classes and extensions for CoreRemoting, providing common helper functionality and extensions.

## Utility Classes

### 🏗️ TaskExtensions
**Namespace:** `CoreRemoting.Toolbox`

Extension methods for Task providing additional functionality for asynchronous operations.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `JustWait(this Task task)` | `void` | Blocks until task completes (synchronously waits) |
| `ExpireMs(this Task task, int timeoutMs)` | `Task` | Adds timeout to task, throws on expiration |
| `Expire(this Task task, TimeSpan timeout)` | `Task` | Adds timeout to task with TimeSpan |
| `Timeout(this Task task, int timeoutMs, string message)` | `Task` | Adds timeout with custom error message |

#### Usage Examples

```csharp
// Synchronously wait for async task
public void SynchronousMethod()
{
    var asyncTask = DoSomethingAsync();
    asyncTask.JustWait(); // Blocks until complete
}

// Add timeout to operation
public async Task<string> GetDataWithTimeoutAsync()
{
    var dataTask = GetDataFromSlowServerAsync();
    return await dataTask.ExpireMs(5000); // 5 second timeout
}

// Timeout with custom message
public async Task ProcessWithTimeoutAsync()
{
    var processTask = LongRunningProcessAsync();
    try
    {
        await processTask.Timeout(30000, "Processing took too long");
    }
    catch (TimeoutException ex)
    {
        Console.WriteLine(ex.Message);
    }
}
```

---

### 🏗️ LimitedSizeQueue<T>
**Namespace:** `CoreRemoting.Toolbox`

Thread-safe queue with limited capacity that provides automatic removal of oldest items when full.

#### Type Parameters

| Parameter | Description |
|-----------|-------------|
| `T` | Type of items stored in queue |

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `Count` | `int` | Gets current number of items |
| `Capacity` | `int` | Gets maximum capacity of queue |
| `IsFull` | `bool` | Gets whether queue is at capacity |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Enqueue(T item)` | `bool` | Adds item, returns false if full (or removes oldest) |
| `Dequeue()` | `T` | Removes and returns oldest item |
| `TryDequeue(out T item)` | `bool` | Tries to dequeue, returns false if empty |
| `Peek()` | `T` | Returns oldest item without removing |
| `TryPeek(out T item)` | `bool` | Tries to peek, returns false if empty |
| `Clear()` | `void` | Removes all items |
| `Contains(T item)` | `bool` | Checks if item exists in queue |

#### Constructors

| Constructor | Description |
|-------------|-------------|
| `LimitedSizeQueue(int capacity)` | Creates queue with specified capacity |
| `LimitedSizeQueue(int capacity, bool removeOldestWhenFull)` | Creates with overflow behavior control |

#### Usage Examples

```csharp
// Basic usage
var queue = new LimitedSizeQueue<string>(capacity: 5);

// Add items
queue.Enqueue("Item1");
queue.Enqueue("Item2");
queue.Enqueue("Item3");

// Remove items
if (queue.TryDequeue(out var item))
{
    Console.WriteLine($"Processed: {item}");
}

// Check capacity
if (queue.IsFull)
{
    Console.WriteLine("Queue is at capacity");
}

// With custom overflow behavior
var criticalQueue = new LimitedSizeQueue<LogEntry>(capacity: 1000, removeOldestWhenFull: true);

criticalQueue.Enqueue(new LogEntry("Warning: System overload"));
// If full, oldest log entry is automatically removed
```

---

## See Also

- [CoreRemoting](CoreRemoting.md) - Core client and server classes
- [CoreRemoting.Threading](CoreRemoting-Threading.md) - Async primitives
- [CoreRemoting.Channels](CoreRemoting-Channels.md) - Transport layer
- [CoreRemoting.RpcMessaging](CoreRemoting-RpcMessaging.md) - Message framework
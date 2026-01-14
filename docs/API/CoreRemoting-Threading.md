# CoreRemoting.Threading Namespace API Reference

This namespace contains async synchronization primitives and threading utilities for CoreRemoting, providing efficient async coordination mechanisms.

## Synchronization Primitives

### 🏗️ AsyncLock
**Namespace:** `CoreRemoting.Threading`

Async-compatible mutual exclusion lock that supports await-based locking without blocking threads.

#### Key Features

- **Async/Await Support**: Fully compatible with async/await patterns
- **No Thread Blocking**: Uses semaphoreSlim instead of Monitor.Enter
- **Fairness**: FIFO ordering for waiters
- **Cancellation Support**: Supports cancellation tokens
- **Reentrant Support**: Same thread can acquire lock multiple times

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `LockAsync(CancellationToken cancellationToken = default)` | `IDisposable` | Acquires lock asynchronously |
| `LockAsync(TimeSpan timeout)` | `IDisposable` | Acquires lock with timeout |
| `TryLockAsync(CancellationToken cancellationToken = default)` | `IDisposable?` | Tries to acquire lock without waiting |

#### Usage Examples

```csharp
// Basic async lock usage
private readonly AsyncLock _lock = new();

public async Task ProcessDataAsync()
{
    using (await _lock.LockAsync())
    {
        // Critical section - only one thread can execute at a time
        await ProcessCriticalOperationAsync();
    }
}

// Lock with timeout
public async Task<bool> TryProcessDataAsync()
{
    using (var lockHandle = await _lock.TryLockAsync())
    {
        if (lockHandle != null)
        {
            await ProcessCriticalOperationAsync();
            return true;
        }
        return false; // Couldn't acquire lock
    }
}

// Lock with cancellation
public async Task ProcessDataWithCancellationAsync(CancellationToken ct)
{
    using (await _lock.LockAsync(ct))
    {
        await ProcessCriticalOperationAsync();
    }
}
```

---

### 🏗️ AsyncManualResetEvent
**Namespace:** `CoreRemoting.Threading`

Async-compatible manual reset event that supports multiple awaiters and manual control.

#### Key Features

- **Async/Await Support**: Awaitable event signal
- **Multiple Waiters**: Multiple coroutines can wait simultaneously
- **Manual Control**: Set and Reset methods for manual control
- **State Tracking**: Current state can be queried

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsSet` | `bool` | Gets whether the event is currently set |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Set()` | `void` | Sets the event, releasing all waiters |
| `Reset()` | `void` | Resets the event, causing future waiters to block |
| `WaitAsync(CancellationToken cancellationToken = default)` | `Task` | Waits for the event to be set |
| `WaitAsync(TimeSpan timeout)` | `Task<bool>` | Waits with timeout |

#### Usage Examples

```csharp
// Async manual reset event usage
private readonly AsyncManualResetEvent _dataReady = new();

public async Task ProduceDataAsync()
{
    // Produce data...
    await GenerateDataAsync();
    
    // Signal consumers that data is ready
    _dataReady.Set();
}

public async Task ConsumeDataAsync()
{
    // Wait for data to be ready
    await _dataReady.WaitAsync();
    
    // Process data
    await ProcessDataAsync();
    
    // Reset for next round
    _dataReady.Reset();
}

// Producer-consumer pattern
public async Task RunProducerConsumerAsync()
{
    var producer = Task.Run(ProduceDataAsync);
    var consumer = Task.Run(ConsumeDataAsync);
    
    await Task.WhenAll(producer, consumer);
}
```

---

### 🏗️ AsyncCountdownEvent
**Namespace:** `CoreRemoting.Threading`

Async-compatible countdown event that signals when a specified number of operations have completed.

#### Key Features

- **Async/Await Support**: Wait for count to reach zero
- **Signal Method**: Increment counter to add more operations
- **Initial Count**: Set initial count in constructor
- **Reuse**: Can be reset and reused

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `CurrentCount` | `int` | Gets the current count |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Signal()` | `void` | Decrements the count by one |
| `AddCount(int count)` | `void` | Increments the count |
| `Reset(int count)` | `void` | Resets to specified count |
| `WaitAsync(CancellationToken cancellationToken = default)` | `Task` | Waits for count to reach zero |

#### Usage Examples

```csharp
// Parallel processing with completion signaling
private readonly AsyncCountdownEvent _completionEvent = new(3);

public async Task ProcessInParallelAsync()
{
    // Start 3 parallel operations
    var tasks = new[]
    {
        ProcessItemAsync(1),
        ProcessItemAsync(2),
        ProcessItemAsync(3)
    };
    
    // Wait for all to signal completion
    var completionTask = _completionEvent.WaitAsync();
    
    // Continue processing
    await Task.WhenAll(tasks.Concat(new[] { completionTask }));
    
    Console.WriteLine("All parallel operations completed");
}

private async Task ProcessItemAsync(int item)
{
    try
    {
        await ProcessSingleItemAsync(item);
    }
    finally
    {
        // Signal completion of this item
        _completionEvent.Signal();
    }
}
```

---

### 🏗️ AsyncReaderWriterLock
**Namespace:** `CoreRemoting.Threading`

Async-compatible reader-writer lock that allows multiple readers or exclusive writer access.

#### Key Features

- **Multiple Readers**: Many concurrent read operations
- **Exclusive Writers**: Single writer with exclusive access
- **Async Support**: All operations support await
- **Upgrade Capability**: Readers can upgrade to writers
- **Fairness**: Balanced scheduling between readers and writers

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `EnterReadLockAsync(CancellationToken cancellationToken = default)` | `IDisposable` | Acquires read lock |
| `EnterWriteLockAsync(CancellationToken cancellationToken = default)` | `IDisposable` | Acquires write lock |
| `TryEnterReadLockAsync()` | `IDisposable?` | Tries to acquire read lock without waiting |
| `TryEnterWriteLockAsync()` | `IDisposable?` | Tries to acquire write lock without waiting |
| `UpgradeToWriteLockAsync()` | `IDisposable` | Upgrades read lock to write lock |

#### Usage Examples

```csharp
// Reader-writer lock usage
private readonly AsyncReaderWriterLock _cacheLock = new();
private readonly Dictionary<string, string> _cache = new();

public async Task<string> GetValueAsync(string key)
{
    using (await _cacheLock.EnterReadLockAsync())
    {
        // Multiple readers can access cache simultaneously
        return _cache.TryGetValue(key, out var value) ? value : null;
    }
}

public async Task SetValueAsync(string key, string value)
{
    using (await _cacheLock.EnterWriteLockAsync())
    {
        // Exclusive access for cache modification
        _cache[key] = value;
    }
}

// Read-modify-write pattern
public async Task UpdateValueAsync(string key, Func<string, string> updater)
{
    // Acquire read lock first
    using (var readLock = await _cacheLock.EnterReadLockAsync())
    {
        var currentValue = _cache.TryGetValue(key, out var val) ? val : null;
        
        // Upgrade to write lock
        using (await _cacheLock.UpgradeToWriteLockAsync())
        {
            _cache[key] = updater(currentValue);
        }
    }
}
```

---

### 🏗️ AsyncCounter
**Namespace:** `CoreRemoting.Threading`

Thread-safe async counter that supports atomic operations and waiting for specific values.

#### Key Features

- **Atomic Operations**: Thread-safe increment/decrement
- **Value Waiting**: Wait for counter to reach specific values
- **Current Value**: Thread-safe access to current count
- **Flexible Operations**: Add, subtract, set operations

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `Value` | `long` | Current counter value |

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Increment()` | `long` | Increments counter and returns new value |
| `Decrement()` | `long` | Decrements counter and returns new value |
| `Add(long amount)` | `long` | Adds amount and returns new value |
| `WaitForValueAsync(long targetValue, CancellationToken cancellationToken = default)` | `Task` | Waits for counter to reach target value |

#### Usage Examples

```csharp
// Async counter for rate limiting
private readonly AsyncCounter _requestCounter = new();

public async Task ProcessRequestAsync()
{
    // Check rate limit (max 100 requests per minute)
    if (_requestCounter.Value >= 100)
    {
        await _requestCounter.WaitForValueAsync(100, 
            CancellationToken.CreateLinkedTokenSource(
                new[] { ct, new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token }).Token);
    }
    
    await _requestCounter.Increment();
    
    // Process request
    await HandleRequestAsync();
    
    // Reset counter every minute
    _ = Task.Delay(TimeSpan.FromMinutes(1)).ContinueWith(_ => _requestCounter.SetValue(0));
}
```

---

## Thread Pool Interface

### 🔄 IThreadPool
**Namespace:** `CoreRemoting.Threading`

Interface for thread pool implementations that provide controlled task execution.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `QueueUserWorkItem(Action callback)` | `void` | Queues work item for execution |
| `QueueUserWorkItem<T>(Action<T> callback, T state)` | `void` | Queues work item with state |
| `GetAvailableThreads()` | `int` | Gets available thread count |
| `GetMaxThreads()` | `int` | Gets maximum thread count |

#### Usage Examples

```csharp
// Custom thread pool implementation
public class MyThreadPool : IThreadPool
{
    private readonly SemaphoreSlim _semaphore;
    private readonly BlockingCollection<Action> _workQueue;
    
    public void QueueUserWorkItem(Action callback)
    {
        _workQueue.Add(callback);
    }
    
    public void QueueUserWorkItem<T>(Action<T> callback, T state)
    {
        _workQueue.Add(() => callback(state));
    }
    
    // Implement other methods...
}
```
---

## See Also

- [CoreRemoting](CoreRemoting.md) - Core client and server classes
- [CoreRemoting.Channels](CoreRemoting-Channels.md) - Transport layer
- [CoreRemoting.RpcMessaging](CoreRemoting-RpcMessaging.md) - Message framework
- [CoreRemoting.Toolbox](CoreRemoting-Toolbox.md) - Additional utilities
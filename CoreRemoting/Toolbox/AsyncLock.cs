using System;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.CompilerServices;

namespace CoreRemoting.Toolbox;

/// <summary>
/// Awaitable async locking synchronization primitive.
/// </summary>
public class AsyncLock
{
    private SemaphoreSlim Semaphore { get; } = new(1, 1);

    /// <summary>
    /// Locks asynchronously, by awaiting the lock object itself.
    /// </summary>
    public TaskAwaiter<IDisposable> GetAwaiter()
    {
        async Task<IDisposable> LockAsync()
        {
            await Semaphore.WaitAsync();
            return Disposable.Create(() => Semaphore.Release());
        }

        return LockAsync().GetAwaiter();
    }
}

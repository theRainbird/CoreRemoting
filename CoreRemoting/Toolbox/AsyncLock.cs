using System;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.CompilerServices;

namespace CoreRemoting.Toolbox;

using ConfiguredAwaiter = ConfiguredTaskAwaitable<IDisposable>.ConfiguredTaskAwaiter;

/// <summary>
/// Awaitable async locking synchronization primitive.
/// </summary>
public class AsyncLock : IDisposable
{
    private SemaphoreSlim Semaphore { get; } = new(1, 1);

    /// <inheritdoc/>
    public void Dispose() => Semaphore.Dispose();

    /// <summary>
    /// Locks asynchronously, by awaiting the lock object itself.
    /// </summary>
    public ConfiguredAwaiter GetAwaiter()
    {
        async Task<IDisposable> LockAsync()
        {
            await Semaphore.WaitAsync()
                .ConfigureAwait(false);

            return Disposable.Create(Semaphore.Release);
        }

        return LockAsync()
            .ConfigureAwait(false)
                .GetAwaiter();
    }
}

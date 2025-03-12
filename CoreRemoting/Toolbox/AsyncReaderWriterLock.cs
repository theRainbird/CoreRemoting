using System;
using System.Threading;
using System.Threading.Tasks;

namespace CoreRemoting.Toolbox;

/// <summary>
/// Simple async readers/writers lock.
/// </summary>
/// <remarks>
/// Based on Michel Raynal's pseudocode using two mutexes and a counter:
/// https://en.wikipedia.org/wiki/Readers–writer_lock#Using_two_mutexes
/// </remarks>
public class AsyncReaderWriterLock : IDisposable
{
    private volatile int blockingReaders = 0;

    private AsyncLock ReadersLock { get; } = new();

    private AsyncLock WritersLock { get; } = new();

    private IDisposable ReleaseWritersLock { get; set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        ReadersLock.Dispose();
        WritersLock.Dispose();
    }

    /// <summary>
    /// Enter the lock in read mode.
    /// </summary>
    public async Task EnterReadLock()
    {
        using (await ReadersLock)
        {
            if (Interlocked.Increment(ref blockingReaders) == 1)
            {
                ReleaseWritersLock = await WritersLock;
            }
        }
    }

    /// <summary>
    /// Reduces the recursion count in read mode and exits read mode if counter is zero.
    /// </summary>
    public async Task ExitReadLock()
    {
        using (await ReadersLock)
        {
            var readerCount = Interlocked.Decrement(ref blockingReaders);
            if (readerCount == 0)
            {
                ReleaseWritersLock?.Dispose();
            }
            else if (readerCount < 0)
            {
                blockingReaders = 0;
                throw new InvalidOperationException("ExitReadLock called before EnterReadLock!");
            }
        }
    }

    /// <summary>
    /// Enter the lock in write mode.
    /// </summary>
    public async Task EnterWriteLock()
    {
        ReleaseWritersLock = await WritersLock;
    }

    /// <summary>
    /// Releases the lock from write mode.
    /// </summary>
    public Task ExitWriteLock()
    {
        var exitLock = ReleaseWritersLock ??
            throw new InvalidOperationException("ExitWriteLock called before EnterWriteLock!");

        exitLock.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Enters the lock in the read mode in an async-disposable way.
    /// </summary>
    internal async Task<IAsyncDisposable> ReadLock()
    {
        await EnterReadLock()
            .ConfigureAwait(false);

        return Disposable.Create(ExitReadLock);
    }

    /// <summary>
    /// Enters the lock in the write mode in an async-disposable way.
    /// </summary>
    internal async Task<IAsyncDisposable> WriteLock()
    {
        await EnterWriteLock()
            .ConfigureAwait(false);

        return Disposable.Create(ExitWriteLock);
    }
}

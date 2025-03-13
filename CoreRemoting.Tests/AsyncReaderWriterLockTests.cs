using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Tests.Tools;
using CoreRemoting.Threading;
using CoreRemoting.Toolbox;
using Xunit;
using Inv = System.InvalidOperationException;

namespace CoreRemoting.Tests;

[Collection("CoreRemoting")]
public class AsyncReaderWriterLockTests
{
    private AsyncReaderWriterLock AsyncLock { get; } = new();

    [Fact]
    public async Task AsyncReaderWriterLock_can_enter_and_exit()
    {
        using var ctx = ValidationSyncContext.Install();
        using var myLock = new AsyncReaderWriterLock();

        await myLock.EnterReadLock();
        await myLock.ExitReadLock();

        await myLock.EnterWriteLock();
        await myLock.ExitWriteLock();
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task AsyncReaderWriterLock_is_compatible_with_await_using()
    {
        using var ctx = ValidationSyncContext.Install();
        using var myLock = new AsyncReaderWriterLock();

        await using (await myLock.Read)
        {
            await Task.Delay(0)
                .ConfigureAwait(false);
        }

        await using (await myLock.Write)
        {
            await Task.Delay(0)
                .ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task AsyncReaderWriterLock_throws_on_exit_before_enter()
    {
        using var ctx = ValidationSyncContext.Install();
        using var myLock = new AsyncReaderWriterLock();

        await Assert.ThrowsAsync<Inv>(myLock.ExitReadLock);
        await Assert.ThrowsAsync<Inv>(myLock.ExitWriteLock);

        var readLock = await myLock.Read;
        await readLock.DisposeAsync();

        await Assert.ThrowsAsync<Inv>(myLock.ExitReadLock);
    }

    [Fact]
    public async Task AsyncReaderWriterLock_simple_multithreaded_test()
    {
        var readers = 0;
        var writers = 0;
        var readerThreads = new ConcurrentDictionary<int, int>();
        var writerThreads = new ConcurrentDictionary<int, int>();

        async Task SimulateRead(int ms)
        {
            readerThreads[Environment.CurrentManagedThreadId] = 0;
            await AsyncLock.EnterReadLock();

            try
            {
                // in a reader's critical section, there are no writers
                Assert.Equal(0, writers);
                Assert.True(readers >= 0);

                Interlocked.Increment(ref readers);
                await Task.Delay(ms);
                Interlocked.Decrement(ref readers);
            }
            finally
            {
                await AsyncLock.ExitReadLock();
            }
        }

        async Task SimulateWrite(int ms)
        {
            writerThreads[Environment.CurrentManagedThreadId] = 0;
            await AsyncLock.EnterWriteLock();

            try
            {
                // in a writer's critical section, there are no readers or writers
                Assert.Equal(0, readers);
                Assert.Equal(0, writers);

                Interlocked.Increment(ref writers);
                await Task.Delay(ms);
                Interlocked.Decrement(ref writers);
            }
            finally
            {
                await AsyncLock.ExitWriteLock();
            }
        }

        const int minDelay = 10;
        const int maxDelay = 100;
        const int count = maxDelay - minDelay;

        // exclusive writers should take ~seconds,
        // parallel readers should take less than seconds
        // so all tasks should end within ~seconds * 2 or less
        var seconds = (minDelay + maxDelay) / 2 * count / 1000;

        var readTasks = Enumerable.Range(minDelay, count).Select(SimulateRead);
        var writeTasks = Enumerable.Range(minDelay, count).Select(SimulateWrite);

        await Task.WhenAll(readTasks.Concat(writeTasks)).Timeout(seconds * 2 + 1);

        // check if it was actually parallelized
        Assert.True(readerThreads.Count > 0);
        Assert.True(writerThreads.Count > 0);
    }

    [Fact]
    public async Task AsyncReaderWriterLock_await_using_multithreaded_test()
    {
        var readers = 0;
        var writers = 0;
        var readerThreads = new ConcurrentDictionary<int, int>();
        var writerThreads = new ConcurrentDictionary<int, int>();

        async Task SimulateRead(int ms)
        {
            readerThreads[Environment.CurrentManagedThreadId] = 0;

            await using (await AsyncLock.Read)
            {
                // in a reader's critical section, there are no writers
                Assert.Equal(0, writers);
                Assert.True(readers >= 0);

                Interlocked.Increment(ref readers);
                await Task.Delay(ms);
                Interlocked.Decrement(ref readers);
            }
        }

        async Task SimulateWrite(int ms)
        {
            writerThreads[Environment.CurrentManagedThreadId] = 0;

            await using (await AsyncLock.Write)
            {
                // in a writer's critical section, there are no readers or writers
                Assert.Equal(0, readers);
                Assert.Equal(0, writers);

                Interlocked.Increment(ref writers);
                await Task.Delay(ms);
                Interlocked.Decrement(ref writers);
            }
        }

        const int minDelay = 10;
        const int maxDelay = 100;
        const int count = maxDelay - minDelay;

        // exclusive writers should take ~seconds,
        // parallel readers should take less than seconds
        // so all tasks should end within ~seconds * 2 or less
        var seconds = (minDelay + maxDelay) / 2 * count / 1000;

        var readTasks = Enumerable.Range(minDelay, count).Select(SimulateRead);
        var writeTasks = Enumerable.Range(minDelay, count).Select(SimulateWrite);

        await Task.WhenAll(readTasks.Concat(writeTasks)).Timeout(seconds * 2 + 1);

        // check if it was actually parallelized
        Assert.True(readerThreads.Count > 0);
        Assert.True(writerThreads.Count > 0);
    }

    [Fact]
    public async Task AsyncReaderWriterLock_doesnt_fail_the_LoadTest()
    {
        // This load test is taken from the AsyncReaderWriterLockSlim unit test suite, but without the sync part:
        // https://github.com/osexpert/AsyncReaderWriterLockSlim/blob/master/AsyncReaderWriterLockSlim.UnitTests/AsyncReaderWriterLockSlimTests.cs#L332
        using var myLock = new AsyncReaderWriterLock();

        var lockCountSyncRoot = new AsyncLock();
        var readLockCount = 0;
        var writeLockCount = 0;
        var incorrectLockCount = 0;

        void checkLockCount()
        {
            Debug.WriteLine($"ReadLocks = {readLockCount}, WriteLocks = {writeLockCount}");

            bool countIsCorrect = readLockCount == 0 && writeLockCount == 0 ||
                readLockCount > 0 && writeLockCount == 0 ||
                readLockCount == 0 && writeLockCount == 1;

            if (!countIsCorrect)
                Interlocked.Increment(ref incorrectLockCount);
        }

        var tasks = new Task[20];
        var cts = new CancellationTokenSource();

        var masterRandom = new Random();

        for (int i = 0; i < tasks.Length; i++)
        {
            var random = new Random(masterRandom.Next());
            tasks[i] = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    var isRead = random.Next(10) < 7;
                    if (isRead)
                        await myLock.EnterReadLock();
                    else
                        await myLock.EnterWriteLock();

                    using (await lockCountSyncRoot)
                    {
                        if (isRead)
                            readLockCount++;
                        else
                            writeLockCount++;

                        checkLockCount();
                    }

                    // Simulate work.
                    await Task.Delay(5 + random.Next(5));

                    using (await lockCountSyncRoot)
                    {
                        if (isRead)
                        {
                            await myLock.ExitReadLock();
                            readLockCount--;
                        }
                        else
                        {
                            await myLock.ExitWriteLock();
                            writeLockCount--;
                        }

                        checkLockCount();
                    }
                }
            });
        }

        // Run for 5 seconds, then stop the tasks
        await Task.Delay(TimeSpan.FromSeconds(5));

        cts.Cancel();

        await Task.WhenAll(tasks).Timeout(1);

        Assert.Equal(0, incorrectLockCount);
    }
}

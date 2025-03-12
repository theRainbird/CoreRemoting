using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Toolbox;
using Xunit;

namespace CoreRemoting.Tests;

[Collection("CoreRemoting")]
public class AsyncReaderWriterLockTests
{
    private AsyncReaderWriterLock AsyncLock { get; } = new();

    [Fact]
    public async Task AsyncReaderWriterLock_can_enter_and_exit()
    {
        var myLock = new AsyncReaderWriterLock();

        await myLock.EnterReadLock();
        await myLock.ExitReadLock();

        await myLock.EnterWriteLock();
        await myLock.ExitWriteLock();
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
    public async Task AsyncReaderWriterLock_doesnt_fail_the_LoadTest()
    {
        // This load test is taken from the AsyncReaderWriterLockSlim unit test suite, but without the sync part:
        // https://github.com/osexpert/AsyncReaderWriterLockSlim/blob/master/AsyncReaderWriterLockSlim.UnitTests/AsyncReaderWriterLockSlimTests.cs#L332
        var myLock = new AsyncReaderWriterLock();

        var lockCountSyncRoot = new AsyncLock();
        var readLockCount = 0;
        var writeLockCount = 0;
        var incorrectLockCount = false;

        void checkLockCount()
        {
            Debug.WriteLine($"ReadLocks = {readLockCount}, WriteLocks = {writeLockCount}");

            bool countIsCorrect = readLockCount == 0 && writeLockCount == 0 ||
                    readLockCount > 0 && writeLockCount == 0 ||
                    readLockCount == 0 && writeLockCount == 1;

            if (!countIsCorrect)
                Volatile.Write(ref incorrectLockCount, true);
        }

        bool cancel = false;

        var tasks = new Task[20];

        var masterRandom = new Random();

        for (int i = 0; i < tasks.Length; i++)
        {
            var random = new Random(masterRandom.Next());
            tasks[i] = Task.Run(async () =>
            {
                while (!Volatile.Read(ref cancel))
                {
                    bool isRead = random.Next(10) < 7;
                    if (isRead)
                        await myLock.EnterReadLock();
                    else
                        await myLock.EnterWriteLock();

                    lock (lockCountSyncRoot)
                    {
                        if (isRead)
                            readLockCount++;
                        else
                            writeLockCount++;

                        checkLockCount();
                    }

                    // Simulate work.
                    await Task.Delay(10);

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

        // Run for 5 seconds, then stop the tasks and threads.
        Thread.Sleep(5000);

        Volatile.Write(ref cancel, true);

        await Task.WhenAll(tasks).Timeout(1);

        Assert.False(incorrectLockCount);
    }
}

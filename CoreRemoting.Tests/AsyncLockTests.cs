using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using CoreRemoting.Tests.Tools;
using CoreRemoting.Threading;
using Xunit;
using Xunit.Sdk;

namespace CoreRemoting.Tests;

[Collection("CoreRemoting")]
public class AsyncLockTests
{
    private AsyncLock Lock { get; } = new();

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task AsyncLock_is_awaitable()
    {
        using var ctx = ValidationSyncContext.Install();

        using (await Lock)
            await Task.Delay(1)
                .ConfigureAwait(false);

        using (await Lock)
            await Task.Delay(2)
                .ConfigureAwait(false);

        using (await Lock)
            await Task.Delay(3)
                .ConfigureAwait(false);
    }

    private async Task RunSharedResourceTest(bool useLock)
    {
        var sharedResource = 0;
        var taskCount = 1000;
        var start = new TaskCompletionSource<bool>();
        var threads = new ConcurrentDictionary<int, bool>();

        async Task Add(int value)
        {
            threads[Environment.CurrentManagedThreadId] = await start.Task;

            async Task<IDisposable> OptionalLock() =>
                useLock ? await Lock : null;

            // access shared resource
            using (await OptionalLock())
            {
                var prevValue = sharedResource;
                await Task.Yield();
                sharedResource = prevValue + value;
            }
        }

        // spawn concurrent tasks
        var tasks = Enumerable
            .Range(1, taskCount)
            .Select(i => Task.Run(() => Add(i)));

        // start all tasks at once
        start.TrySetResult(true);
        await Task.WhenAll(tasks);

        // debugging
        Console.WriteLine($"Shared resource test: use lock = {useLock}");
        Console.WriteLine($"Shared resource test: thread count = {threads.Count}");
        Console.WriteLine($"Shared resource test: result = {sharedResource}");

        // check if there were many threads involved
        Assert.True(threads.Count > 1);

        // validate the calculation: 1 + 2 + ... + taskCount
        Assert.Equal((taskCount + 1) * taskCount / 2, sharedResource);
    }

    [Fact]
    public async Task AsyncLock_protects_shared_resource_from_concurrent_access()
    {
        await RunSharedResourceTest(useLock: true);
    }

    [Fact]
    public async Task Missing_AsyncLock_doesnt_protect_shared_resource_from_concurrent_access()
    {
        await Assert.ThrowsAsync<EqualException>(async () =>
            await RunSharedResourceTest(useLock: false));
    }
}

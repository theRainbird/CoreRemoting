using CoreRemoting.Toolbox;
using System;
using System.Threading.Tasks;
using Xunit;

namespace CoreRemoting.Tests;

partial class DisposableTests
{
    [Fact]
    public async Task AsyncDisposable_executes_action_on_DisposeAsync()
    {
        var disposed = false;

        async Task DisposeTask()
        {
            await Task.Yield();
            disposed = true;
        }

        await using (Disposable.Create(DisposeTask))
            Assert.False(disposed);

        Assert.True(disposed);
    }

    [Fact]
    public async Task AsyncTaskDisposable_executes_action_on_DisposeAsync()
    {
        var disposed = false;

        async ValueTask DisposeAsync()
        {
            await Task.Yield();
            disposed = true;
        }

        await using (Disposable.Create(DisposeAsync))
            Assert.False(disposed);

        Assert.True(disposed);
    }

    [Fact]
    public async Task Disposable_combines_async_disposables()
    {
        var count = 0;
        Task AsyncDispose() =>
            Task.FromResult(count++);

        void Dispose() =>
            count++;

        var d1 = Disposable.Create(AsyncDispose);
        var d2 = Disposable.Create(AsyncDispose);
        var d3 = Disposable.Create(Dispose);
        var d4 = Disposable.Create(AsyncDispose);
        var d5 = Disposable.Create(Dispose);

        await using (Disposable.Create(d1, d2, d3, d4, d5))
            Assert.Equal(0, count);

        Assert.Equal(5, count);
    }

    [Fact]
    public async Task TryDisposeAsync_ignores_nulls()
    {
        IAsyncDisposable disposable = null;

        await disposable.TryDisposeAsync(); // doesn't throw
    }

    [Fact]
    public async Task TryDisposeAsync_disposes_object()
    {
        var disposed = false;
        async Task disposeTask()
        {
            await Task.Yield();
            disposed = true;
        }

        var disposable = Disposable.Create(disposeTask);
        await disposable.TryDisposeAsync();

        Assert.True(disposed);
    }

    [Fact]
    public async Task TryDisposeAsync_ignores_exceptions()
    {
        static async Task disposeTask()
        {
            await Task.Yield();
            throw new InvalidOperationException("Test exception");
        }

        var disposable = Disposable.Create(disposeTask);
        await disposable.TryDisposeAsync(); // doesn't throw
    }
}
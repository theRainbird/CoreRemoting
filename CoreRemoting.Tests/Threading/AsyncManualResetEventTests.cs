using System;
using System.Threading.Tasks;
using CoreRemoting.Channels.Null;
using CoreRemoting.Threading;
using CoreRemoting.Toolbox;
using Xunit;

namespace CoreRemoting.Tests.Threading;

/// <summary>
/// Fixture for cleaning up static state between tests.
/// </summary>
public class NullMessageQueueCleanupFixture : IDisposable
{
    public void Dispose()
    {
        NullMessageQueue.ClearAll();
    }
}

/// <summary>
/// Collection for AsyncManualResetEvent tests with cleanup.
/// </summary>
[CollectionDefinition("AsyncManualResetEventTests")]
public class AsyncManualResetEventTestsCollection : ICollectionFixture<NullMessageQueueCleanupFixture>
{
}

/// <summary>
/// Unit tests based on Nito.AsyncEx unit tests for the AsyncManualResetEvent class by Stephen Cleary:
/// https://github.com/StephenCleary/AsyncEx/blob/master/test/AsyncEx.Coordination.UnitTests/AsyncManualResetEventUnitTests.cs
/// </summary>
[Collection("AsyncManualResetEventTests")]
public class AsyncManualResetEventTests
{
    private async Task NeverCompletes(Task task, double sec = 0.5) =>
        await Assert.ThrowsAsync<TimeoutException>(() => task.Timeout(sec));

    [Fact]
    public async Task WaitAsync_Unset_IsNotCompleted()
    {
        var mre = new AsyncManualResetEvent();

        var task = mre.WaitAsync();

        await NeverCompletes(mre.WaitAsync());
    }

    [Fact]
    public void WaitAsync_AfterSet_IsCompleted()
    {
        var mre = new AsyncManualResetEvent();

        mre.Set();
        var task = mre.WaitAsync();

        Assert.True(task.IsCompleted);
    }

    [Fact]
    public void WaitAsync_Set_IsCompleted()
    {
        var mre = new AsyncManualResetEvent(true);

        var task = mre.WaitAsync();

        Assert.True(task.IsCompleted);
    }

    [Fact]
    public void MultipleWaitAsync_AfterSet_IsCompleted()
    {
        var mre = new AsyncManualResetEvent();

        mre.Set();
        var task1 = mre.WaitAsync();
        var task2 = mre.WaitAsync();

        Assert.True(task1.IsCompleted);
        Assert.True(task2.IsCompleted);
    }

    [Fact]
    public void MultipleWaitAsync_Set_IsCompleted()
    {
        var mre = new AsyncManualResetEvent(true);

        var task1 = mre.WaitAsync();
        var task2 = mre.WaitAsync();

        Assert.True(task1.IsCompleted);
        Assert.True(task2.IsCompleted);
    }

    [Fact]
    public async Task WaitAsync_AfterReset_IsNotCompleted()
    {
        var mre = new AsyncManualResetEvent();

        mre.Set();
        mre.Reset();
        var task = mre.WaitAsync();

        await NeverCompletes(task);
    }
}

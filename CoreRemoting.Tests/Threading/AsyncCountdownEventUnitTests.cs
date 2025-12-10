using System;
using System.Threading.Tasks;
using CoreRemoting.Threading;
using Xunit;

namespace CoreRemoting.Tests.Threading;

/// <summary>
/// Unit tests based on Nito.AsyncEx unit tests for the AsyncCountdownEvent class by Stephen Cleary:
/// https://github.com/StephenCleary/AsyncEx/blob/master/test/AsyncEx.Coordination.UnitTests/AsyncCountdownEventUnitTests.cs
/// </summary>
[Collection("AsyncManualResetEventTests")]
public class AsyncCountdownEventUnitTests
{
    [Fact]
    public async Task WaitAsync_Unset_IsNotCompleted()
    {
        var ce = new AsyncCountdownEvent(1);
        var task = ce.WaitAsync();

        Assert.Equal(1, ce.CurrentCount);
        Assert.False(task.IsCompleted);

        ce.Signal();
        await task;
    }

    [Fact]
    public void WaitAsync_Set_IsCompleted()
    {
        var ce = new AsyncCountdownEvent(0);
        var task = ce.WaitAsync();

        Assert.Equal(0, ce.CurrentCount);
        Assert.True(task.IsCompleted);
    }

    [Fact]
    public async Task AddCount_IncrementsCount()
    {
        var ce = new AsyncCountdownEvent(1);
        var task = ce.WaitAsync();
        Assert.Equal(1, ce.CurrentCount);
        Assert.False(task.IsCompleted);

        ce.AddCount();

        Assert.Equal(2, ce.CurrentCount);
        Assert.False(task.IsCompleted);

        ce.Signal(2);
        await task;
    }

    [Fact]
    public async Task Signal_Nonzero_IsNotCompleted()
    {
        var ce = new AsyncCountdownEvent(2);
        var task = ce.WaitAsync();
        Assert.False(task.IsCompleted);

        ce.Signal();

        Assert.Equal(1, ce.CurrentCount);
        Assert.False(task.IsCompleted);

        ce.Signal();
        await task;
    }

    [Fact]
    public void Signal_Zero_SynchronouslyCompletesWaitTask()
    {
        var ce = new AsyncCountdownEvent(1);
        var task = ce.WaitAsync();
        Assert.False(task.IsCompleted);

        ce.Signal();

        Assert.Equal(0, ce.CurrentCount);
        Assert.True(task.IsCompleted);
    }

    [Fact]
    public async Task Signal_AfterSet_CountsNegativeAndResetsTask()
    {
        var ce = new AsyncCountdownEvent(0);
        var originalTask = ce.WaitAsync();

        ce.Signal();

        var newTask = ce.WaitAsync();
        Assert.Equal(-1, ce.CurrentCount);
        Assert.NotSame(originalTask, newTask);

        ce.AddCount();
        await newTask;
    }

    [Fact]
    public async Task AddCount_AfterSet_CountsPositiveAndResetsTask()
    {
        var ce = new AsyncCountdownEvent(0);
        var originalTask = ce.WaitAsync();

        ce.AddCount();
        var newTask = ce.WaitAsync();

        Assert.Equal(1, ce.CurrentCount);
        Assert.NotSame(originalTask, newTask);

        ce.Signal();
        await newTask;
    }

    [Fact]
    public async Task Signal_PastZero_PulsesTask()
    {
        var ce = new AsyncCountdownEvent(1);
        var originalTask = ce.WaitAsync();

        ce.Signal(2);
        await originalTask;
        var newTask = ce.WaitAsync();

        Assert.Equal(-1, ce.CurrentCount);
        Assert.NotSame(originalTask, newTask);

        ce.AddCount();
        await newTask;
    }

    [Fact]
    public async Task AddCount_PastZero_PulsesTask()
    {
        var ce = new AsyncCountdownEvent(-1);
        var originalTask = ce.WaitAsync();

        ce.AddCount(2);
        await originalTask;
        var newTask = ce.WaitAsync();

        Assert.Equal(1, ce.CurrentCount);
        Assert.NotSame(originalTask, newTask);

        ce.Signal();
        await newTask;
    }

    [Fact]
    public void AddCount_Overflow_ThrowsException()
    {
        var ce = new AsyncCountdownEvent(long.MaxValue);
        Assert.Throws<OverflowException>(() => ce.AddCount());
    }

    [Fact]
    public void Signal_Underflow_ThrowsException()
    {
        var ce = new AsyncCountdownEvent(long.MinValue);
        Assert.Throws<OverflowException>(() => ce.Signal());
    }
}

using System;
using System.Threading.Tasks;

namespace CoreRemoting.Threading;

/// <summary>
/// Awaitable counterpart of the CountdownEvent class.
/// </summary>
/// <remarks>
/// Based on the work of Stephen Toub and Stephen Cleary:
/// https://devblogs.microsoft.com/pfxteam/building-async-coordination-primitives-part-3-asynccountdownevent/
/// https://github.com/StephenCleary/AsyncEx/blob/master/src/Nito.AsyncEx.Coordination/AsyncCountdownEvent.cs
/// </remarks>
public class AsyncCountdownEvent
{
    private readonly AsyncManualResetEvent _mre;

    private long _count;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncCountdownEvent"/> class.
    /// </summary>
    public AsyncCountdownEvent(long initialCount = 0)
    {
        _mre = new AsyncManualResetEvent(initialCount == 0);
        _count = initialCount;
    }

    /// <summary>
    /// Performs the requested getter function within the critical section.
    /// </summary>
    /// <typeparam name="T">The type of the getter returned value.</typeparam>
    private T Synced<T>(Func<T> getter)
    {
        lock (_mre)
        {
            return getter();
        }
    }

    /// <summary>
    /// Gets the current number of remaining signals before this event becomes set.
    /// This member is seldom used; code using this member has a high possibility of race conditions.
    /// </summary>
    public long CurrentCount => Synced(() => _count);

    /// <summary>
    /// Waits for the count to reach zero.
    /// </summary>
    public Task WaitAsync() => _mre.WaitAsync();

    /// <summary>
    /// Attempts to modify the current count by the specified amount.
    /// </summary>
    /// <param name="delta">The amount to change the current count.</param>
    private void ModifyCount(long delta)
    {
        if (delta == 0)
            return;

        lock (_mre)
        {
            var oldCount = _count;
            checked
            {
                _count += delta;
            }

            if (oldCount == 0)
            {
                _mre.Reset();
            }
            else if (_count == 0)
            {
                _mre.Set();
            }
            else if ((oldCount < 0 && _count > 0) || (oldCount > 0 && _count < 0))
            {
                _mre.Set();
                _mre.Reset();
            }
        }
    }

    /// <summary>
    /// Adds the specified value to the current count.
    /// </summary>
    /// <param name="addCount">The amount to change the current count.</param>
    public void AddCount(long addCount = 1) =>
        ModifyCount(addCount);

    /// <summary>
    /// Subtracts the specified value from the current count.
    /// </summary>
    /// <param name="signalCount">The amount to change the current count.</param>
    public void Signal(long signalCount = 1) =>
        ModifyCount(-signalCount);
}

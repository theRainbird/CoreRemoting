using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoreRemoting.Threading;

/// <summary>
/// Awaitable counterpart of the ManualResetEvent class.
/// </summary>
/// <remarks>
/// Based on the work of Stephen Toub and Stephen Cleary:
/// https://devblogs.microsoft.com/pfxteam/building-async-coordination-primitives-part-1-asyncmanualresetevent/
/// https://github.com/StephenCleary/AsyncEx
/// </remarks>
public class AsyncManualResetEvent
{
    private readonly object syncObject = new();

    private TaskCompletionSource<bool> tcs = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncManualResetEvent"/> class.
    /// </summary>
    public AsyncManualResetEvent(bool set = false)
    {
        if (set)
        {
            tcs.TrySetResult(true);
        }
    }

    /// <summary>
    /// Performs the requested getter function within the critical section.
    /// </summary>
    /// <typeparam name="T">The type of the getter returned value.</typeparam>
    private T Synced<T>(Func<T> getter)
    {
        lock (syncObject)
        {
            return getter();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the event is currently set.
    /// </summary>
    public bool IsSet => Synced(() => tcs.Task.IsCompleted);

    /// <summary>
    /// Waits for the event to be set.
    /// </summary>
    public Task WaitAsync() => Synced(() => tcs.Task);

    /// <summary>
    /// Sets the event. If it's already set, does nothing.
    /// </summary>
    public void Set() => Synced(() => tcs.TrySetResult(true));

    /// <summary>
    /// Resets the event. If it's already reset, does nothing.
    /// </summary>
    public void Reset() => Synced(() => tcs = IsSet ? new() : tcs);
}

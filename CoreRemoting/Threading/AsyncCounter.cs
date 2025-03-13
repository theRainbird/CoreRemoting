using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace CoreRemoting.Threading;

/// <summary>
/// Threadsafe counter that can await for the given value.
/// </summary>
public class AsyncCounter
{
    private ConcurrentDictionary<int, TaskCompletionSource<int>> Sources { get; } = [];

    private int value;

    /// <summary>
    /// Gets the current value of the counter.
    /// </summary>
    public int Value => value;

    /// <summary>
    /// Increments the counter.
    /// </summary>
    public AsyncCounter Increment()
    {
        var v = Interlocked.Increment(ref value);

        if (Sources.TryGetValue(v, out var tcs) && tcs != null)
        {
            tcs.TrySetResult(v);
        }

        return this;
    }

    /// <summary>
    /// Returns the task that can be awaited
    /// for the counter to get to the specified value.
    /// </summary>
    public Task<int> WaitForValue(int value)
    {
        var tcs = Sources.GetOrAdd(value, i => new());
        if (value <= Value)
        {
            tcs.TrySetResult(value);
        }

        return tcs.Task;
    }

    /// <summary>
    /// Increments the counter.
    /// </summary>
    public static AsyncCounter operator ++(AsyncCounter ac) =>
        ac.Increment();
}

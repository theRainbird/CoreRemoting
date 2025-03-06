using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace CoreRemoting.Toolbox;

/// <summary>
/// Threadsafe counter that can await for the given value.
/// </summary>
public class AsyncCounter
{
    private ConcurrentDictionary<int, TaskCompletionSource<int>> Sources { get; } = [];

    private int value;

    public int Value => value;

    public AsyncCounter Increment()
    {
        var v = Interlocked.Increment(ref value);

        if (Sources.TryGetValue(v, out var tcs) && tcs != null)
        {
            tcs.TrySetResult(v);
        }

        return this;
    }

    public Task<int> WaitForValue(int value)
    {
        var tcs = Sources.GetOrAdd(value, i => new());
        if (value <= Value)
        {
            tcs.TrySetResult(value);
        }

        return tcs.Task;
    }

    public static AsyncCounter operator ++ (AsyncCounter ac) =>
        ac.Increment();
}

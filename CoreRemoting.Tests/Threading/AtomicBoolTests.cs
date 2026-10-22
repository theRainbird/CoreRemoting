using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Threading;
using Xunit;

namespace CoreRemoting.Tests.Threading;

public class AtomicBoolTests
{
    [Fact]
    public void Default_value_is_false()
    {
        var flag = new AtomicBool();

        Assert.False(flag.Value);
    }

    [Fact]
    public void Set_makes_value_true()
    {
        var flag = new AtomicBool();

        flag.Set();

        Assert.True(flag.Value);
    }

    [Fact]
    public void Set_is_idempotent()
    {
        var flag = new AtomicBool();

        flag.Set();
        flag.Set();
        flag.Set();

        Assert.True(flag.Value);
    }

    [Fact]
    public void Reset_makes_value_false()
    {
        var flag = new AtomicBool();
        flag.Set();

        flag.Reset();

        Assert.False(flag.Value);
    }

    [Fact]
    public void Reset_on_fresh_flag_is_noop()
    {
        var flag = new AtomicBool();

        flag.Reset();

        Assert.False(flag.Value);
    }

    [Fact]
    public void Reset_is_idempotent()
    {
        var flag = new AtomicBool();

        flag.Reset();
        flag.Reset();
        flag.Reset();

        Assert.False(flag.Value);
    }

    [Fact]
    public void TrySet_returns_true_on_first_call()
    {
        var flag = new AtomicBool();

        Assert.True(flag.TrySet());
        Assert.True(flag.Value);
    }

    [Fact]
    public void TrySet_returns_false_on_second_call()
    {
        var flag = new AtomicBool();

        Assert.True(flag.TrySet());
        Assert.False(flag.TrySet());
        Assert.True(flag.Value);
    }

    [Fact]
    public void TrySet_after_Set_returns_false()
    {
        var flag = new AtomicBool();
        flag.Set();

        Assert.False(flag.TrySet());
        Assert.True(flag.Value);
    }

    [Fact]
    public void TrySet_returns_true_again_after_clear()
    {
        var flag = new AtomicBool();
        flag.TrySet();
        flag.Reset();

        Assert.True(flag.TrySet());
        Assert.True(flag.Value);
    }

    [Fact]
    public async Task Concurrent_TrySet_leaves_exactly_one_winner()
    {
        var flag = new AtomicBool();
        var winners = 0;

        await Task.WhenAll(
            Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
            {
                if (flag.TrySet())
                    Interlocked.Increment(ref winners);
            })));

        Assert.Equal(1, winners);
        Assert.True(flag.Value);
    }

    [Fact]
    public async Task Concurrent_access_does_not_throw()
    {
        var flag = new AtomicBool();
        var stop = false;

        var setter = Task.Run(() =>
        {
            while (!Volatile.Read(ref stop))
                flag.Set();
        });

        var clearer = Task.Run(() =>
        {
            while (!Volatile.Read(ref stop))
                flag.Reset();
        });

        var trySetter = Task.Run(() =>
        {
            while (!Volatile.Read(ref stop))
                flag.TrySet();
        });

        var reader = Task.Run(() =>
        {
            while (!Volatile.Read(ref stop))
                _ = flag.Value;
        });

        await Task.Delay(100);
        Volatile.Write(ref stop, true);
        await Task.WhenAll(setter, clearer, trySetter, reader);
    }

    [Fact]
    public void TryReset_returns_false_on_fresh_flag()
    {
        var flag = new AtomicBool();

        Assert.False(flag.TryReset());
        Assert.False(flag.Value);
    }

    [Fact]
    public void TryReset_returns_true_after_set()
    {
        var flag = new AtomicBool();
        flag.Set();

        Assert.True(flag.TryReset());
        Assert.False(flag.Value);
    }

    [Fact]
    public void TryReset_returns_false_on_second_call()
    {
        var flag = new AtomicBool();
        flag.Set();

        Assert.True(flag.TryReset());
        Assert.False(flag.TryReset());
        Assert.False(flag.Value);
    }

    [Fact]
    public void TryReset_after_try_set_returns_true()
    {
        var flag = new AtomicBool();
        flag.TrySet();

        Assert.True(flag.TryReset());
        Assert.False(flag.Value);
    }

    [Fact]
    public async Task Concurrent_TryReset_leaves_exactly_one_winner()
    {
        var flag = new AtomicBool();
        flag.Set();
        var winners = 0;

        await Task.WhenAll(
            Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
            {
                if (flag.TryReset())
                    Interlocked.Increment(ref winners);
            })));

        Assert.Equal(1, winners);
        Assert.False(flag.Value);
    }
}
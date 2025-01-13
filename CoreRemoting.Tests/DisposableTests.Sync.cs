using CoreRemoting.Toolbox;
using System;
using Xunit;

namespace CoreRemoting.Tests;

public partial class DisposableTests
{
    [Fact]
    public void Disposable_executes_action_on_Dispose()
    {
        var disposed = false;

        void Dispose() =>
            disposed = true;

        using (Disposable.Create(Dispose))
            Assert.False(disposed);

        Assert.True(disposed);
    }

    [Fact]
    public void Disposable_ignores_nulls()
    {
        Action dispose = null;

        using (Disposable.Create(dispose))
        {
            // doesn't throw
        }
    }

    [Fact]
    public void Disposable_combines_disposables()
    {
        var count = 0;
        void Dispose() =>
            count++;

        var d1 = Disposable.Create(Dispose);
        var d2 = Disposable.Create(Dispose);
        var d3 = Disposable.Create(Dispose);

        using (Disposable.Create(d1, d2, d3))
            Assert.Equal(0, count);

        Assert.Equal(3, count);
    }
}
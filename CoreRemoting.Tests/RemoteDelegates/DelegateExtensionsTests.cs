using System;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.Toolbox;
using Xunit;

namespace CoreRemoting.Tests.RemoteDelegates;

public class DelegateExtensionsTests
{
    [Fact]
    public void IsDisposed_returns_true_for_null()
    {
        Delegate del = null;

        Assert.True(del.IsDisposed());
    }

    [Fact]
    public void IsDisposed_returns_false_for_regular_delegate()
    {
        Action del = () => { };

        Assert.False(del.IsDisposed());
    }

    [Fact]
    public void IsDisposed_returns_false_for_multicast_delegate()
    {
        Action a = () => { };
        Action b = () => { };
        var combined = a + b;

        Assert.False(combined.IsDisposed());
    }

    [Fact]
    public void IsDisposed_returns_false_for_live_proxy_delegate()
    {
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(Action), _ => null);

        Assert.False(proxy.ProxiedDelegate.IsDisposed());
    }

    [Fact]
    public void IsDisposed_returns_true_for_disposed_proxy_delegate()
    {
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(Action), _ => null);
        var del = proxy.ProxiedDelegate;

        proxy.Dispose();

        Assert.True(del.IsDisposed());
    }

    [Fact]
    public void IsDisposed_returns_false_for_combined_delegate_with_proxied_part()
    {
        // multicast containing a proxy — conservatively reported as not disposed
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(Action), _ => null);
        Action regular = () => { };

        var combined = (Action)proxy.ProxiedDelegate + regular;

        Assert.False(combined.IsDisposed());
    }
}

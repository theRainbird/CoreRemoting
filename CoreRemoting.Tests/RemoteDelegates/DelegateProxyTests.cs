using System;
using System.Threading.Tasks;
using CoreRemoting.RemoteDelegates;
using Xunit;

namespace CoreRemoting.Tests.RemoteDelegates;

public class DelegateProxyTests
{
    // --- Factory validation ---

    [Fact]
    public void Create_throws_on_null_delegate_type()
    {
        var factory = new DelegateProxyFactory();

        Assert.Throws<ArgumentException>(() => factory.Create(null, _ => null));
    }

    [Fact]
    public void Create_throws_on_non_delegate_type()
    {
        var factory = new DelegateProxyFactory();

        Assert.Throws<ArgumentException>(() => factory.Create(typeof(string), _ => null));
    }

    [Fact]
    public void Create_throws_on_null_handler()
    {
        var factory = new DelegateProxyFactory();

        Assert.Throws<ArgumentNullException>(() => factory.Create(typeof(Action), null));
    }

    // --- Delegate shape ---

    [Fact]
    public void ProxiedDelegate_has_correct_type()
    {
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(EventHandler), _ => null);

        Assert.IsType<EventHandler>(proxy.ProxiedDelegate);
    }

    [Fact]
    public void ProxiedDelegate_target_is_the_proxy_instance()
    {
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(Action), _ => null);

        Assert.Same(proxy, proxy.ProxiedDelegate.Target);
    }

    // --- Argument passing ---

    [Fact]
    public void ProxiedDelegate_invokes_handler_with_empty_array_for_no_params()
    {
        var factory = new DelegateProxyFactory();
        object[] captured = null;
        var proxy = factory.Create(typeof(Action), args => { captured = args; return null; });

        ((Action)proxy.ProxiedDelegate)();

        Assert.NotNull(captured);
        Assert.Empty(captured);
    }

    [Fact]
    public void ProxiedDelegate_passes_arguments_in_order()
    {
        var factory = new DelegateProxyFactory();
        object[] captured = null;
        var proxy = factory.Create(typeof(Action<int, string, bool>), args => { captured = args; return null; });

        ((Action<int, string, bool>)proxy.ProxiedDelegate)(42, "hello", true);

        Assert.NotNull(captured);
        Assert.Equal(3, captured.Length);
        Assert.Equal(42, captured[0]);
        Assert.Equal("hello", captured[1]);
        Assert.Equal(true, captured[2]);
    }

    [Fact]
    public void ProxiedDelegate_boxes_value_type_arguments()
    {
        var factory = new DelegateProxyFactory();
        object captured = null;
        var proxy = factory.Create(typeof(Action<int>), args => { captured = args[0]; return null; });

        ((Action<int>)proxy.ProxiedDelegate)(42);

        Assert.IsType<int>(captured);
        Assert.Equal(42, captured);
    }

    [Fact]
    public void ProxiedDelegate_preserves_null_arguments()
    {
        var factory = new DelegateProxyFactory();
        object captured = "sentinel";
        var proxy = factory.Create(typeof(Action<object>), args => { captured = args[0]; return null; });

        ((Action<object>)proxy.ProxiedDelegate)(null);

        Assert.Null(captured);
    }

    // --- Return value passing ---

    [Fact]
    public void ProxiedDelegate_returns_handler_value_for_reference_return()
    {
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(Func<string>), _ => "hello");

        var result = ((Func<string>)proxy.ProxiedDelegate)();

        Assert.Equal("hello", result);
    }

    [Fact]
    public void ProxiedDelegate_unboxes_value_type_return_value()
    {
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(Func<int>), _ => 42);

        var result = ((Func<int>)proxy.ProxiedDelegate)();

        Assert.IsType<int>(result);
        Assert.Equal(42, result);
    }

    [Fact]
    public void ProxiedDelegate_throws_when_handler_returns_wrong_type_for_value_return()
    {
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(Func<int>), _ => "not an int");

        Assert.Throws<InvalidCastException>(() => ((Func<int>)proxy.ProxiedDelegate)());
    }

    [Fact]
    public async Task ProxiedDelegate_supports_async_returns()
    {
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(Func<Task<string>>), _ => Task.FromResult("hello"));

        var result = await ((Func<Task<string>>)proxy.ProxiedDelegate)();

        Assert.Equal("hello", result);
    }

    // --- Independence ---

    [Fact]
    public void Multiple_proxies_from_same_factory_are_independent()
    {
        var factory = new DelegateProxyFactory();
        var invokedA = false;
        var invokedB = false;
        var proxyA = factory.Create(typeof(Action), _ => { invokedA = true; return null; });
        var proxyB = factory.Create(typeof(Action), _ => { invokedB = true; return null; });

        ((Action)proxyA.ProxiedDelegate)();

        Assert.True(invokedA);
        Assert.False(invokedB);
    }

    [Fact]
    public void Proxies_of_different_types_can_coexist()
    {
        var factory = new DelegateProxyFactory();
        var proxy1 = factory.Create(typeof(Action), _ => null);
        var proxy2 = factory.Create(typeof(Func<int>), _ => 42);
        var proxy3 = factory.Create(typeof(EventHandler), _ => null);

        Assert.IsType<Action>(proxy1.ProxiedDelegate);
        Assert.IsType<Func<int>>(proxy2.ProxiedDelegate);
        Assert.IsType<EventHandler>(proxy3.ProxiedDelegate);
    }

    // --- Dispose behavior ---

    [Fact]
    public void Dispose_clears_proxied_delegate()
    {
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(Action), _ => null);

        proxy.Dispose();

        Assert.Null(proxy.ProxiedDelegate);
    }

    [Fact]
    public void Invoking_delegate_after_dispose_is_safe_for_void_return()
    {
        var factory = new DelegateProxyFactory();
        var invoked = false;
        var proxy = factory.Create(typeof(Action), _ => { invoked = true; return null; });
        var del = (Action)proxy.ProxiedDelegate;

        proxy.Dispose();
        del(); // handler was nulled by Dispose; invocation must be a no-op

        Assert.False(invoked);
    }
}

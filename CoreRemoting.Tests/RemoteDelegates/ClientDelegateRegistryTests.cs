using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using CoreRemoting.RemoteDelegates;
using Xunit;

namespace CoreRemoting.Tests.RemoteDelegates;

public class ClientDelegateRegistryTests
{
    private sealed class FakeProxy
    {
    }

    [Fact]
    public void Register_returns_non_empty_guid()
    {
        var registry = new ClientDelegateRegistry();
        Action handler = () => { };
        var proxy = new FakeProxy();

        var key = registry.RegisterClientDelegate(handler, proxy);

        Assert.NotEqual(Guid.Empty, key);
    }

    [Fact]
    public void Register_throws_on_null_delegate()
    {
        var registry = new ClientDelegateRegistry();

        Assert.Throws<ArgumentNullException>(() =>
            registry.RegisterClientDelegate(null, new FakeProxy()));
    }

    [Fact]
    public void Register_throws_on_null_service_proxy()
    {
        var registry = new ClientDelegateRegistry();
        Action handler = () => { };

        Assert.Throws<ArgumentNullException>(() =>
            registry.RegisterClientDelegate(handler, null));
    }

    [Fact]
    public void Register_same_delegate_for_same_proxy_returns_same_guid()
    {
        var registry = new ClientDelegateRegistry();
        Action handler = () => { };
        var proxy = new FakeProxy();

        var key1 = registry.RegisterClientDelegate(handler, proxy);
        var key2 = registry.RegisterClientDelegate(handler, proxy);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void Register_same_delegate_for_different_proxies_returns_different_guids()
    {
        // The same delegate subscribed to events of two different proxies
        // must be tracked as two independent registrations.
        var registry = new ClientDelegateRegistry();
        Action handler = () => { };
        var proxy1 = new FakeProxy();
        var proxy2 = new FakeProxy();

        var key1 = registry.RegisterClientDelegate(handler, proxy1);
        var key2 = registry.RegisterClientDelegate(handler, proxy2);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void Register_different_delegates_returns_different_guids()
    {
        var registry = new ClientDelegateRegistry();
        Action handler1 = () => { };
        Action handler2 = () => { };
        var proxy = new FakeProxy();

        var key1 = registry.RegisterClientDelegate(handler1, proxy);
        var key2 = registry.RegisterClientDelegate(handler2, proxy);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void Register_different_delegates_of_same_method_returns_different_guids()
    {
        var registry = new ClientDelegateRegistry();
        var target1 = new CallbackTarget();
        var target2 = new CallbackTarget();
        var proxy = new FakeProxy();

        // Same method name, different targets -> different delegates.
        var key1 = registry.RegisterClientDelegate(target1.OnEvent, proxy);
        var key2 = registry.RegisterClientDelegate(target2.OnEvent, proxy);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GetDelegateByHandlerKey_returns_registered_delegate()
    {
        var registry = new ClientDelegateRegistry();
        Action handler = () => { };
        var proxy = new FakeProxy();

        var key = registry.RegisterClientDelegate(handler, proxy);
        var retrieved = registry.GetDelegateByHandlerKey(key);

        Assert.Same(handler, retrieved);
    }

    [Fact]
    public void GetDelegateByHandlerKey_returns_null_for_unknown_key()
    {
        var registry = new ClientDelegateRegistry();

        var retrieved = registry.GetDelegateByHandlerKey(Guid.NewGuid());

        Assert.Null(retrieved);
    }

    [Fact]
    public void UnregisterClientDelegatesOfServiceProxy_removes_only_matching_ones()
    {
        var registry = new ClientDelegateRegistry();
        var proxy1 = new FakeProxy();
        var proxy2 = new FakeProxy();

        Action handler1 = () => { };
        Action handler2 = () => { };
        Action handler3 = () => { };

        var key1 = registry.RegisterClientDelegate(handler1, proxy1);
        var key2 = registry.RegisterClientDelegate(handler2, proxy1);
        var key3 = registry.RegisterClientDelegate(handler3, proxy2);

        registry.UnregisterClientDelegatesOfServiceProxy(proxy1);

        Assert.Null(registry.GetDelegateByHandlerKey(key1));
        Assert.Null(registry.GetDelegateByHandlerKey(key2));
        Assert.Same(handler3, registry.GetDelegateByHandlerKey(key3));
    }

    [Fact]
    public void UnregisterClientDelegatesOfServiceProxy_keeps_same_delegate_on_other_proxy()
    {
        // Regression test: the same delegate subscribed to two proxies must
        // survive the shutdown of one of them.
        var registry = new ClientDelegateRegistry();
        var proxy1 = new FakeProxy();
        var proxy2 = new FakeProxy();

        Action handler = () => { };

        var key1 = registry.RegisterClientDelegate(handler, proxy1);
        var key2 = registry.RegisterClientDelegate(handler, proxy2);

        registry.UnregisterClientDelegatesOfServiceProxy(proxy1);

        Assert.Null(registry.GetDelegateByHandlerKey(key1));
        Assert.Same(handler, registry.GetDelegateByHandlerKey(key2));
    }

    [Fact]
    public void UnregisterClientDelegatesOfServiceProxy_with_null_is_noop()
    {
        var registry = new ClientDelegateRegistry();
        Action handler = () => { };
        var proxy = new FakeProxy();

        var key = registry.RegisterClientDelegate(handler, proxy);
        registry.UnregisterClientDelegatesOfServiceProxy(null);

        Assert.Same(handler, registry.GetDelegateByHandlerKey(key));
    }

    [Fact]
    public void UnregisterClientDelegatesOfServiceProxy_for_unknown_proxy_is_noop()
    {
        var registry = new ClientDelegateRegistry();
        Action handler = () => { };
        var proxy = new FakeProxy();

        var key = registry.RegisterClientDelegate(handler, proxy);
        registry.UnregisterClientDelegatesOfServiceProxy(new FakeProxy());

        Assert.Same(handler, registry.GetDelegateByHandlerKey(key));
    }

    [Fact]
    public void Clear_removes_all_registrations()
    {
        var registry = new ClientDelegateRegistry();
        var proxy = new FakeProxy();

        Action handler1 = () => { };
        Action handler2 = () => { };

        var key1 = registry.RegisterClientDelegate(handler1, proxy);
        var key2 = registry.RegisterClientDelegate(handler2, proxy);

        registry.Clear();

        Assert.Null(registry.GetDelegateByHandlerKey(key1));
        Assert.Null(registry.GetDelegateByHandlerKey(key2));
    }

    [Fact]
    public async Task Register_same_delegate_concurrently_returns_same_guid()
    {
        var registry = new ClientDelegateRegistry();
        Action handler = () => { };
        var proxy = new FakeProxy();
        var keys = new ConcurrentBag<Guid>();

        await Task.WhenAll(
            Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
                keys.Add(registry.RegisterClientDelegate(handler, proxy)))));

        Assert.Single(keys.Distinct());
    }

    [Fact]
    public async Task Register_same_delegate_concurrently_for_two_proxies_returns_two_guids()
    {
        var registry = new ClientDelegateRegistry();
        Action handler = () => { };
        var proxy1 = new FakeProxy();
        var proxy2 = new FakeProxy();
        var keys = new ConcurrentBag<Guid>();

        await Task.WhenAll(
            Enumerable.Range(0, 50).SelectMany(_ => new[]
            {
                Task.Run(() => keys.Add(registry.RegisterClientDelegate(handler, proxy1))),
                Task.Run(() => keys.Add(registry.RegisterClientDelegate(handler, proxy2))),
            }));

        // Two distinct guids, 50 occurrences each.
        Assert.Equal(2, keys.Distinct().Count());
    }

    private sealed class CallbackTarget
    {
        public void OnEvent()
        {
        }
    }
}

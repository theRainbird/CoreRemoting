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
    public void Register_same_delegate_twice_returns_same_guid()
    {
        var registry = new ClientDelegateRegistry();
        Action handler = () => { };
        var proxy = new FakeProxy();

        var key1 = registry.RegisterClientDelegate(handler, proxy);
        var key2 = registry.RegisterClientDelegate(handler, proxy);

        Assert.Equal(key1, key2);
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
    public void FindDelegate_returns_registered_guid()
    {
        var registry = new ClientDelegateRegistry();
        Action handler = () => { };
        var proxy = new FakeProxy();

        var key = registry.RegisterClientDelegate(handler, proxy);
        var found = registry.FindDelegate(handler);

        Assert.Equal(key, found);
    }

    [Fact]
    public void FindDelegate_returns_empty_for_unregistered_delegate()
    {
        var registry = new ClientDelegateRegistry();
        Action handler = () => { };

        var found = registry.FindDelegate(handler);

        Assert.Equal(Guid.Empty, found);
    }

    [Fact]
    public void FindDelegate_returns_empty_for_null()
    {
        var registry = new ClientDelegateRegistry();

        var found = registry.FindDelegate(null);

        Assert.Equal(Guid.Empty, found);
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
    public void GetInfoByHandlerKey_returns_registration_info()
    {
        var registry = new ClientDelegateRegistry();
        Action handler = () => { };
        var proxy = new FakeProxy();

        var key = registry.RegisterClientDelegate(handler, proxy);
        var info = registry.GetDelegateInfoByHandlerKey(key);

        Assert.NotNull(info);
        Assert.Equal(key, info.HandlerKey);
        Assert.Same(handler, info.ClientDelegate);
        Assert.Same(proxy, info.ServiceProxy);
    }

    [Fact]
    public void GetInfoByHandlerKey_returns_null_for_unknown_key()
    {
        var registry = new ClientDelegateRegistry();

        var info = registry.GetDelegateInfoByHandlerKey(Guid.NewGuid());

        Assert.Null(info);
    }

    [Fact]
    public void Unregister_by_handler_key_removes_registration()
    {
        var registry = new ClientDelegateRegistry();
        Action handler = () => { };
        var proxy = new FakeProxy();

        var key = registry.RegisterClientDelegate(handler, proxy);
        registry.UnregisterClientDelegate(key);

        Assert.Null(registry.GetDelegateByHandlerKey(key));
        Assert.Equal(Guid.Empty, registry.FindDelegate(handler));
    }

    [Fact]
    public void Unregister_by_handler_key_is_noop_for_unknown_key()
    {
        var registry = new ClientDelegateRegistry();

        registry.UnregisterClientDelegate(Guid.NewGuid());
        // no exception expected
    }

    [Fact]
    public void Unregister_by_delegate_removes_registration()
    {
        var registry = new ClientDelegateRegistry();
        Action handler = () => { };
        var proxy = new FakeProxy();

        var key = registry.RegisterClientDelegate(handler, proxy);
        registry.UnregisterClientDelegate(handler);

        Assert.Null(registry.GetDelegateByHandlerKey(key));
        Assert.Equal(Guid.Empty, registry.FindDelegate(handler));
    }

    [Fact]
    public void Unregister_by_null_delegate_is_noop()
    {
        var registry = new ClientDelegateRegistry();

        registry.UnregisterClientDelegate(null);
        // no exception expected
    }

    [Fact]
    public void Unregister_by_unregistered_delegate_is_noop()
    {
        var registry = new ClientDelegateRegistry();
        Action handler = () => { };

        registry.UnregisterClientDelegate(handler);
        // no exception expected
    }

    [Fact]
    public void Unregister_after_removal_allows_reregistration_with_new_guid()
    {
        var registry = new ClientDelegateRegistry();
        Action handler = () => { };
        var proxy = new FakeProxy();

        var key1 = registry.RegisterClientDelegate(handler, proxy);
        registry.UnregisterClientDelegate(handler);
        var key2 = registry.RegisterClientDelegate(handler, proxy);

        Assert.NotEqual(key1, key2);
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

        Assert.Equal(Guid.Empty, registry.FindDelegate(handler1));
        Assert.Equal(Guid.Empty, registry.FindDelegate(handler2));
        Assert.Equal(key3, registry.FindDelegate(handler3));
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
        Assert.Equal(Guid.Empty, registry.FindDelegate(handler1));
        Assert.Equal(Guid.Empty, registry.FindDelegate(handler2));
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
    public async Task Register_and_unregister_concurrently_doesnt_corrupt_registry()
    {
        var registry = new ClientDelegateRegistry();
        var proxy = new FakeProxy();

        var handlers = Enumerable.Range(0, 50)
            .Select(_ => (Action)(() => { }))
            .ToArray();

        await Task.WhenAll(
            handlers.SelectMany(handler => new[]
            {
                Task.Run(() => registry.RegisterClientDelegate(handler, proxy)),
                Task.Run(() => registry.UnregisterClientDelegate(handler)),
            }));

        // Regardless of ordering, the registry must be consistent:
        // every remaining key resolves to a delegate, and reverse lookups agree.
        foreach (var handler in handlers)
        {
            var key = registry.FindDelegate(handler);
            if (key != Guid.Empty)
            {
                Assert.Same(handler, registry.GetDelegateByHandlerKey(key));
            }
        }
    }

    private sealed class CallbackTarget
    {
        public void OnEvent()
        {
        }
    }
}

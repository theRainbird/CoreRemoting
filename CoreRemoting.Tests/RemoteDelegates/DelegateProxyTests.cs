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

        Assert.Throws<ArgumentNullException>(() => factory.Create(null, _ => null));
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
    public void Invoking_delegate_after_dispose_throws_ObjectDisposedException_for_void_return()
    {
        var factory = new DelegateProxyFactory();
        var invoked = false;
        var proxy = factory.Create(typeof(Action), _ => { invoked = true; return null; });
        var del = (Action)proxy.ProxiedDelegate;
        del();
        Assert.True(invoked);

        proxy.Dispose();
        Assert.Throws<ObjectDisposedException>(() => del());
    }

    [Fact]
    public void Invoking_delegate_after_dispose_throws_ObjectDisposedException_for_value_return()
    {
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(Func<int>), _ => 42);
        var del = (Func<int>)proxy.ProxiedDelegate;

        proxy.Dispose();

        Assert.Throws<ObjectDisposedException>(() => del());
    }

    [Fact]
    public void Invoking_delegate_after_dispose_throws_ObjectDisposedException_for_reference_return()
    {
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(Func<string>), _ => "hello");
        var del = (Func<string>)proxy.ProxiedDelegate;

        proxy.Dispose();

        Assert.Throws<ObjectDisposedException>(() => del());
    }

    [Fact]
    public void Dispose_can_be_called_multiple_times()
    {
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(Action), _ => null);

        proxy.Dispose();
        proxy.Dispose();
        proxy.Dispose();
    }

    // --- Exception propagation ---

    [Fact]
    public void ProxiedDelegate_propagates_handler_exception_without_wrapping()
    {
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(Action), _ => throw new InvalidOperationException("boom"));

        var ex = Assert.Throws<InvalidOperationException>(() => ((Action)proxy.ProxiedDelegate)());

        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public void ProxiedDelegate_propagates_handler_exception_for_value_return_without_wrapping()
    {
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(Func<int>), _ => throw new InvalidOperationException("boom"));

        var ex = Assert.Throws<InvalidOperationException>(() => ((Func<int>)proxy.ProxiedDelegate)());

        Assert.Equal("boom", ex.Message);
    }

    // --- Repeated invocation ---

    [Fact]
    public void ProxiedDelegate_can_be_invoked_multiple_times()
    {
        var factory = new DelegateProxyFactory();
        var count = 0;
        var proxy = factory.Create(typeof(Action), _ => { count++; return null; });
        var del = (Action)proxy.ProxiedDelegate;

        del();
        del();
        del();

        Assert.Equal(3, count);
    }

    // --- Reference return: null value ---

    [Fact]
    public void ProxiedDelegate_returns_null_for_reference_return()
    {
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(Func<string>), _ => null);

        Assert.Null(((Func<string>)proxy.ProxiedDelegate)());
    }

    // --- Nullable<T> ---

    [Fact]
    public void ProxiedDelegate_unboxes_nullable_value_return()
    {
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(Func<int?>), _ => 42);

        var result = ((Func<int?>)proxy.ProxiedDelegate)();

        Assert.True(result.HasValue);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ProxiedDelegate_returns_null_nullable_for_null_handler_result()
    {
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(Func<int?>), _ => null);

        var result = ((Func<int?>)proxy.ProxiedDelegate)();

        Assert.False(result.HasValue);
    }

    // --- Enum ---

    [Fact]
    public void ProxiedDelegate_boxes_enum_arguments()
    {
        var factory = new DelegateProxyFactory();
        object captured = null;
        var proxy = factory.Create(typeof(Action<DayOfWeek>), args => { captured = args[0]; return null; });

        ((Action<DayOfWeek>)proxy.ProxiedDelegate)(DayOfWeek.Wednesday);

        Assert.Equal(DayOfWeek.Wednesday, captured);
    }

    [Fact]
    public void ProxiedDelegate_unboxes_enum_return()
    {
        var factory = new DelegateProxyFactory();
        var proxy = factory.Create(typeof(Func<DayOfWeek>), _ => DayOfWeek.Wednesday);

        var result = ((Func<DayOfWeek>)proxy.ProxiedDelegate)();

        Assert.Equal(DayOfWeek.Wednesday, result);
    }

    // --- Non-generic Task identity ---

    [Fact]
    public async Task ProxiedDelegate_returns_task_as_is()
    {
        var factory = new DelegateProxyFactory();
        var tcs = new TaskCompletionSource<bool>();
        var proxy = factory.Create(typeof(Func<Task>), _ => tcs.Task);

        var returnedTask = ((Func<Task>)proxy.ProxiedDelegate)();

        Assert.Same(tcs.Task, returnedTask);

        tcs.SetResult(true);
        await returnedTask;
    }

    // --- Reference-type interface parameter ---

    [Fact]
    public void ProxiedDelegate_passes_interface_arguments_without_boxing()
    {
        var factory = new DelegateProxyFactory();
        object captured = null;
        var disposable = new System.IO.MemoryStream();
        var proxy = factory.Create(typeof(Action<IDisposable>), args => { captured = args[0]; return null; });

        ((Action<IDisposable>)proxy.ProxiedDelegate)(disposable);

        Assert.Same(disposable, captured);
    }

    // --- Edge Cases and IL Coverage ---

    [Fact]
    public void ProxiedDelegate_pops_handler_value_when_delegate_return_is_void()
    {
        var factory = new DelegateProxyFactory();
        // Delegate is Action (void), but the handler returns a string.
        // This ensures the IL emits OpCodes.Pop correctly without stack corruption.
        var proxy = factory.Create(typeof(Action), _ => "this value should be popped");
        var del = (Action)proxy.ProxiedDelegate;

        var ex = Record.Exception(() => del());

        Assert.Null(ex);
    }

    [Fact]
    public void ProxiedDelegate_handles_many_arguments_correctly()
    {
        var factory = new DelegateProxyFactory();
        object[] captured = null;
        // Delegate with 8 arguments of different types to test index generation and array population.
        var proxy = factory.Create(typeof(Action<int, string, bool, double, char, byte, short, long>),
            args => { captured = args; return null; });

        ((Action<int, string, bool, double, char, byte, short, long>)proxy.ProxiedDelegate)
            (1, "2", true, 4.0, '5', 6, 7, 8L);

        Assert.NotNull(captured);
        Assert.Equal(8, captured.Length);
        Assert.Equal(1, captured[0]);
        Assert.Equal("2", captured[1]);
        Assert.Equal(8L, captured[7]);
    }

    [Fact]
    public void ProxiedDelegate_boxes_and_unboxes_complex_value_types()
    {
        var factory = new DelegateProxyFactory();
        var originalGuid = Guid.NewGuid();
        object capturedArg = null;

        var proxy = factory.Create(typeof(Action<Guid>), args => { capturedArg = args[0]; return null; });
        ((Action<Guid>)proxy.ProxiedDelegate)(originalGuid);

        Assert.IsType<Guid>(capturedArg);
        Assert.Equal(originalGuid, capturedArg);

        var returnProxy = factory.Create(typeof(Func<Guid>), _ => originalGuid);
        var result = ((Func<Guid>)returnProxy.ProxiedDelegate)();

        Assert.Equal(originalGuid, result);
    }

    [Fact]
    public void ProxiedDelegate_preserves_array_instance_argument()
    {
        var factory = new DelegateProxyFactory();
        object[] captured = null;
        var proxy = factory.Create(typeof(Action<string, object[]>), args => { captured = args; return null; });

        var myArray = new object[] { 1, 2, 3 };
        ((Action<string, object[]>)proxy.ProxiedDelegate)("test", myArray);

        Assert.NotNull(captured);
        Assert.Equal(2, captured.Length);
        Assert.Equal("test", captured[0]);
        Assert.Same(myArray, captured[1]); // Must be the exact same array instance.
    }

    [Fact]
    public void ProxiedDelegate_can_be_combined_with_other_delegates()
    {
        var factory = new DelegateProxyFactory();
        var proxyInvoked = false;
        var regularInvoked = false;

        var proxy = factory.Create(typeof(Action), _ => { proxyInvoked = true; return null; });
        Action regularAction = () => regularInvoked = true;

        // Combine the proxy and a regular delegate (multicast).
        Action combined = (Action)proxy.ProxiedDelegate + regularAction;

        combined();

        Assert.True(proxyInvoked);
        Assert.True(regularInvoked);
    }

    // --- Unsupported signatures: ref/out/in parameters ---

    [Fact]
    public void Create_throws_on_delegate_with_ref_parameter()
    {
        var factory = new DelegateProxyFactory();

        Assert.Throws<NotSupportedException>(() =>
            factory.Create(typeof(RefAction), _ => null));
    }

    [Fact]
    public void Create_throws_on_delegate_with_out_parameter()
    {
        var factory = new DelegateProxyFactory();

        Assert.Throws<NotSupportedException>(() =>
            factory.Create(typeof(OutAction), _ => null));
    }

    [Fact]
    public void Create_throws_on_delegate_with_in_parameter()
    {
        var factory = new DelegateProxyFactory();

        Assert.Throws<NotSupportedException>(() =>
            factory.Create(typeof(InAction), _ => null));
    }

    // --- Unsupported signatures: open generic delegate types ---

    [Fact]
    public void Create_throws_on_open_generic_delegate_type()
    {
        var factory = new DelegateProxyFactory();

        Assert.Throws<ArgumentException>(() =>
            factory.Create(typeof(Action<>), _ => null));
    }

    // Helper delegate types for unsupported signature tests

    private delegate void RefAction(ref int value);
    private delegate void OutAction(out int value);
    private delegate void InAction(in int value);

    // --- Dynamic method caching ---

    [Fact]
    public void Factory_reuses_dynamic_method_for_same_delegate_type()
    {
        var factory = new DelegateProxyFactory();

        var proxy1 = (DelegateProxy)factory.Create(typeof(Action), _ => null);
        var proxy2 = (DelegateProxy)factory.Create(typeof(Action), _ => null);

        Assert.Same(proxy1.DynamicMethod, proxy2.DynamicMethod);
    }

    [Fact]
    public void Factory_caches_dynamic_method_even_when_handlers_differ()
    {
        var factory = new DelegateProxyFactory();

        var proxy1 = (DelegateProxy)factory.Create(typeof(Action), _ => null);
        var proxy2 = (DelegateProxy)factory.Create(typeof(Action), _ => { throw new InvalidOperationException(); return null; });

        Assert.Same(proxy1.DynamicMethod, proxy2.DynamicMethod);
        Assert.NotSame(proxy1, proxy2);
    }

    [Fact]
    public void Factory_creates_separate_dynamic_methods_for_different_delegate_types()
    {
        var factory = new DelegateProxyFactory();

        var action = (DelegateProxy)factory.Create(typeof(Action), _ => null);
        var funcInt = (DelegateProxy)factory.Create(typeof(Func<int>), _ => null);
        var eventHandler = (DelegateProxy)factory.Create(typeof(EventHandler), _ => null);

        Assert.NotSame(action.DynamicMethod, funcInt.DynamicMethod);
        Assert.NotSame(action.DynamicMethod, eventHandler.DynamicMethod);
        Assert.NotSame(funcInt.DynamicMethod, eventHandler.DynamicMethod);
    }
}

using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.Threading;
using CoreRemoting.Toolbox;
using Xunit;
using static CoreRemoting.RemoteDelegates.ServiceEventHub;

namespace CoreRemoting.Tests.RemoteDelegates;

public class ServiceEventHubTests
{
    public interface ISampleInterface
    {
        string FireHandlers(int argument);

        event EventHandler SimpleEvent;

        event EventHandler<CancelEventArgs> CancelEvent;

        Action ActionDelegate { get; set; }

        Func<int, string> FuncDelegate { get; set; }
    }

    public interface ISampleDescendant1 : ISampleInterface
    {
        event EventHandler NewEvent;

        Action NewDelegate { get; set; }
    }

    public interface ISampleDescendant2 : ISampleDescendant1, ISampleInterface
    {
        event EventHandler<CancelEventArgs> NewCancelEvent;
    }

    public interface ISampleUnrelated
    {
        event EventHandler SomeOtherEvent;
    }

    public class SampleService : ISampleInterface
    {
        public string FireHandlers(int argument)
        {
            if (SimpleEvent != null)
            {
                SimpleEvent(this, EventArgs.Empty);
            }

            if (CancelEvent != null)
            {
                CancelEvent(this, new CancelEventArgs());
            }

            if (ActionDelegate != null)
            {
                ActionDelegate();
            }

            if (FuncDelegate != null)
            {
                return FuncDelegate(argument);
            }

            return null;
        }

        public event EventHandler SimpleEvent;

        public event EventHandler<CancelEventArgs> CancelEvent;

        public Action ActionDelegate { get; set; }

        public Func<int, string> FuncDelegate { get; set; }
    }

    [Fact]
    public void ServiceEventHub_Contains_Events_And_Delegates()
    {
        var eventHub = new ServiceEventHub(typeof(ISampleInterface));
        Assert.NotNull(eventHub[nameof(ISampleInterface.SimpleEvent)]);
        Assert.NotNull(eventHub[nameof(ISampleInterface.CancelEvent)]);
        Assert.NotNull(eventHub[nameof(ISampleInterface.ActionDelegate)]);
        Assert.NotNull(eventHub[nameof(ISampleInterface.FuncDelegate)]);
    }

    [Fact]
    public void ServiceEventHub_Contains_Inherited_Events_And_Delegates()
    {
        var eventHub = new ServiceEventHub(typeof(ISampleDescendant2));
        Assert.NotNull(eventHub[nameof(ISampleDescendant2.NewCancelEvent)]);
        Assert.NotNull(eventHub[nameof(ISampleDescendant2.NewEvent)]);
        Assert.NotNull(eventHub[nameof(ISampleDescendant2.NewDelegate)]);
        Assert.NotNull(eventHub[nameof(ISampleDescendant2.SimpleEvent)]);
        Assert.NotNull(eventHub[nameof(ISampleDescendant2.CancelEvent)]);
        Assert.NotNull(eventHub[nameof(ISampleDescendant2.ActionDelegate)]);
        Assert.NotNull(eventHub[nameof(ISampleDescendant2.FuncDelegate)]);
    }

    [Fact]
    public void ServiceEventHub_Delegates_Have_Same_Types_As_Their_Originals()
    {
        var eventHub = new ServiceEventHub(typeof(ISampleInterface));
        Assert.IsType<EventHandler>(eventHub[nameof(ISampleInterface.SimpleEvent)]);
        Assert.IsType<EventHandler<CancelEventArgs>>(eventHub[nameof(ISampleInterface.CancelEvent)]);
        Assert.IsType<Action>(eventHub[nameof(ISampleInterface.ActionDelegate)]);
        Assert.IsType<Func<int, string>>(eventHub[nameof(ISampleInterface.FuncDelegate)]);
    }

    [Fact]
    public async Task ServiceEventHub_Simple_Handle_Tests()
    {
        // add the first handler
        var eventHub = new ServiceEventHub(typeof(ISampleInterface));
        var fired = new AsyncCounter();
        eventHub.AddHandler(nameof(ISampleInterface.SimpleEvent), Guid.NewGuid(), Guid.Empty, new EventHandler((sender, args) => fired++));

        // check if it is called
        var handler = (EventHandler)eventHub[nameof(ISampleInterface.SimpleEvent)];
        handler(this, EventArgs.Empty);
        Assert.Equal(1, await fired.WaitForValue(1).Timeout(1));

        // add the second handler
        var firedAgain = new AsyncCounter();
        var secondKey = Guid.NewGuid();
        var tempHandler = new EventHandler((sender, args) => firedAgain++);
        eventHub.AddHandler(nameof(ISampleInterface.SimpleEvent), secondKey, Guid.Empty, tempHandler);

        // check if it is called
        handler(this, EventArgs.Empty);
        Assert.Equal(2, await fired.WaitForValue(2).Timeout(1));
        Assert.Equal(1, await firedAgain.WaitForValue(1).Timeout(1));

        // remove the second handler
        eventHub.RemoveHandler(nameof(ISampleInterface.SimpleEvent), secondKey);

        // check if it is not called
        handler(this, EventArgs.Empty);
        Assert.Equal(3, await fired.WaitForValue(3).Timeout(1));
        Assert.False(await firedAgain.WaitForValue(2).Expire(0.1));
    }

    [Fact]
    public async Task ServiceEventHub_Cancel_Event_Tests()
    {
        // add the first handler
        var eventHub = new ServiceEventHub(typeof(ISampleInterface));
        var fired = new AsyncCounter();
        eventHub.AddHandler(nameof(ISampleInterface.CancelEvent), Guid.NewGuid(), Guid.Empty, new EventHandler<CancelEventArgs>((sender, args) => fired++));

        // check if it is called
        var handler = (EventHandler<CancelEventArgs>)eventHub[nameof(ISampleInterface.CancelEvent)];
        handler(this, new CancelEventArgs());
        Assert.Equal(1, await fired.WaitForValue(1).Timeout(1));

        // add the second handler
        var firedAgain = new AsyncCounter();
        var secondKey = Guid.NewGuid();
        var tempHandler = new EventHandler<CancelEventArgs>((sender, args) => firedAgain++);
        eventHub.AddHandler(nameof(ISampleInterface.CancelEvent), secondKey, Guid.Empty, tempHandler);

        // check if it is called
        handler(this, new CancelEventArgs());
        Assert.Equal(2, await fired.WaitForValue(2).Timeout(1));
        Assert.Equal(1, await firedAgain.WaitForValue(1).Timeout(1));

        // remove the second handler
        eventHub.RemoveHandler(nameof(ISampleInterface.CancelEvent), secondKey);

        // check if it is not called
        handler(this, new CancelEventArgs());
        Assert.Equal(3, await fired.WaitForValue(3).Timeout(1));
        Assert.False(await firedAgain.WaitForValue(2).Expire(0.1));
    }

    [Fact]
    public async Task ServiceEventHub_ActionDelegateTests()
    {
        // add the first handler
        var eventHub = new ServiceEventHub(typeof(ISampleInterface));
        var fired = new AsyncCounter();
        eventHub.AddHandler(nameof(ISampleInterface.ActionDelegate), Guid.NewGuid(), Guid.Empty, new Action(() => fired++));

        // check if it is called
        var handler = (Action)eventHub[nameof(ISampleInterface.ActionDelegate)];
        handler();
        Assert.Equal(1, await fired.WaitForValue(1).Timeout(1));

        // add the second handler
        var firedAgain = new AsyncCounter();
        var secondKey = Guid.NewGuid();
        var tempHandler = new Action(() => firedAgain++);
        eventHub.AddHandler(nameof(ISampleInterface.ActionDelegate), secondKey, Guid.Empty, tempHandler);

        // check if it is called
        handler();
        Assert.Equal(2, await fired.WaitForValue(2).Timeout(1));
        Assert.Equal(1, await firedAgain.WaitForValue(1).Timeout(1));

        // remove the second handler
        eventHub.RemoveHandler(nameof(ISampleInterface.ActionDelegate), secondKey);

        // check if it is not called
        handler();
        Assert.Equal(3, await fired.WaitForValue(3).Timeout(1));
        Assert.False(await firedAgain.WaitForValue(2).Expire(0.1));
    }

    [Fact]
    public async Task ServiceEventHub_FuncDelegateTests()
    {
        // add the first handler
        var eventHub = new ServiceEventHub(typeof(ISampleInterface));
        var fired = new AsyncCounter();
        eventHub.AddHandler(nameof(ISampleInterface.FuncDelegate), Guid.NewGuid(), Guid.Empty, new Func<int, string>(a =>
        {
            fired++;
            return a.ToString(); // discarded
        }));

        // check if it is called
        var handler = (Func<int, string>)eventHub[nameof(ISampleInterface.FuncDelegate)];
        var result = handler(123);
        Assert.Equal(1, await fired.WaitForValue(1).Timeout(1));
        Assert.Null(result); // Assert.Equal("123", result);

        // add the second handler
        var firedAgain = new AsyncCounter();
        var secondKey = Guid.NewGuid();
        var tempHandler = new Func<int, string>(a =>
        {
            firedAgain++;
            return a.ToString(); // discarded
        });

        eventHub.AddHandler(nameof(ISampleInterface.FuncDelegate), secondKey, Guid.Empty, tempHandler);

        // check if it is called
        result = handler(321);
        Assert.Equal(2, await fired.WaitForValue(2).Timeout(1));
        Assert.Equal(1, await firedAgain.WaitForValue(1).Timeout(1));
        Assert.Null(result); // Assert.Equal("123", result);

        // remove the second handler
        eventHub.RemoveHandler(nameof(ISampleInterface.FuncDelegate), secondKey);

        // check if it is not called
        result = handler(0);
        Assert.Equal(3, await fired.WaitForValue(3).Timeout(1));
        Assert.False(await firedAgain.WaitForValue(2).Expire(0.1));
        Assert.Null(result); // Assert.Equal("0", result);
    }

    [Fact]
    public async Task ServiceEventHub_WireUnwireTests()
    {
        var eventHub = new ServiceEventHub(typeof(ISampleInterface));
        var simpleEventFired = new AsyncCounter();
        var cancelEventFired = new AsyncCounter();
        var actionFired = new AsyncCounter();
        var funcFired = new AsyncCounter();

        // add event handlers
        eventHub.AddHandler(nameof(ISampleInterface.SimpleEvent), Guid.NewGuid(), Guid.Empty, new EventHandler((sender, args) => simpleEventFired++));
        eventHub.AddHandler(nameof(ISampleInterface.CancelEvent), Guid.NewGuid(), Guid.Empty, new EventHandler<CancelEventArgs>((sender, args) => cancelEventFired++));
        eventHub.AddHandler(nameof(ISampleInterface.ActionDelegate), Guid.NewGuid(), Guid.Empty, new Action(() => actionFired++));
        eventHub.AddHandler(nameof(ISampleInterface.FuncDelegate), Guid.NewGuid(), Guid.Empty, new Func<int, string>(a => { funcFired++; return a.ToString(); }));
        eventHub.AddHandler(nameof(ISampleInterface.FuncDelegate), Guid.NewGuid(), Guid.Empty, new Func<int, string>(a => { funcFired++; return (a * 2).ToString(); }));

        // wire up events
        var component = new SampleService();
        eventHub.WireTo(component);

        // test if it works
        var result = component.FireHandlers(102030);
        Assert.Null(result); // Assert.Equal("204060", result);

        // check if all events were fired
        var results = await Task.WhenAll(
            simpleEventFired.WaitForValue(1),
            cancelEventFired.WaitForValue(1),
            actionFired.WaitForValue(1),
            funcFired.WaitForValue(2)).Timeout(1);

        Assert.Equal(5, results.Sum());

        // unwire
        eventHub.UnwireFrom(component);

        // test if it works
        result = component.FireHandlers(123);

        // event handlers shoundn't fire anymore
        Assert.Null(result);
        Assert.False(await simpleEventFired.WaitForValue(2).Expire(0.1));
        Assert.False(await cancelEventFired.WaitForValue(2).Expire(0.1));
        Assert.False(await actionFired.WaitForValue(2).Expire(0.1));
        Assert.False(await funcFired.WaitForValue(3).Expire(0.1));
    }

    [Fact]
    public void ServiceEventHub_Handler_Count_Tests()
    {
        var eventHub = new ServiceEventHub(typeof(ISampleInterface));
        var sampleService = new SampleService();

        eventHub.WireTo(sampleService);
        Assert.Equal(0, eventHub.GetHandlerCount(nameof(ISampleInterface.SimpleEvent)));

        eventHub.AddHandler(nameof(ISampleInterface.SimpleEvent), Guid.NewGuid(), Guid.Empty, new EventHandler((s, e) => { }));
        Assert.Equal(1, eventHub.GetHandlerCount(nameof(ISampleInterface.SimpleEvent)));

        var secondKey = Guid.NewGuid();
        var handler = new EventHandler((s, e) => { });

        eventHub.AddHandler(nameof(ISampleInterface.SimpleEvent), secondKey, Guid.Empty, handler);
        Assert.Equal(2, eventHub.GetHandlerCount(nameof(ISampleInterface.SimpleEvent)));

        eventHub.RemoveHandler(nameof(ISampleInterface.SimpleEvent), secondKey);
        Assert.Equal(1, eventHub.GetHandlerCount(nameof(ISampleInterface.SimpleEvent)));
    }

    [Fact]
    public void ServiceEventHub_doesnt_throw_if_no_subscriptions_exist()
    {
        var eventHub = new ServiceEventHub(typeof(ISampleInterface));
        var sampleService = new SampleService();
        eventHub.WireTo(sampleService);
        sampleService.FireHandlers(1);
    }

    [Fact]
    public void MethodInfo_can_represent_subscription_or_unsubscription()
    {
        var method = typeof(ISampleInterface).GetMethod("add_SimpleEvent");
        Assert.NotNull(method);
        Assert.True(method.IsEventAccessor(out var eventName, out var subscription));
        Assert.Equal(nameof(ISampleInterface.SimpleEvent), eventName);
        Assert.True(subscription);

        method = typeof(ISampleInterface).GetMethod("remove_CancelEvent");
        Assert.NotNull(method);
        Assert.True(method.IsEventAccessor(out eventName, out subscription));
        Assert.Equal(nameof(ISampleInterface.CancelEvent), eventName);
        Assert.False(subscription);

        method = typeof(ISampleInterface).GetMethod(nameof(ISampleInterface.FireHandlers));
        Assert.NotNull(method);
        Assert.False(method.IsEventAccessor(out eventName, out subscription));
        Assert.Null(eventName);
        Assert.False(subscription);
    }

    [Fact]
    public async Task TypedEventHub_can_invoke_events()
    {
        var simpleEventFired = new AsyncCounter();
        void simpleHandler(object sender, EventArgs args) =>
            simpleEventFired.Increment();
        var simpleEventHandler = (EventHandler)simpleHandler;

        // add event handler
        var typedHub = new TypedEventHub<ISampleInterface>();
        var key = Guid.NewGuid();
        typedHub.AddHandler(s => nameof(s.SimpleEvent), key, Guid.Empty, simpleEventHandler);

        // invoke event handler
        typedHub.Invoke(s => nameof(s.SimpleEvent), this, EventArgs.Empty);
        await simpleEventFired.WaitForValue(1).Timeout(1);

        // invoke by string name
        typedHub.Invoke(nameof(ISampleInterface.SimpleEvent), this, EventArgs.Empty);
        await simpleEventFired.WaitForValue(2).Timeout(1);

        // remove event handler
        typedHub.RemoveHandler(s => nameof(s.SimpleEvent), key);
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await simpleEventFired.WaitForValue(3).Timeout(1));
    }

    // --- TypedEventHub wrapper tests ---

    [Fact]
    public void TypedEventHub_creates_its_own_hub()
    {
        var typedHub = new TypedEventHub<ISampleInterface>();

        Assert.NotNull(typedHub.Hub);
        Assert.Equal(typeof(ISampleInterface), typedHub.Hub.InterfaceType);
    }

    [Fact]
    public void TypedEventHub_wraps_existing_hub()
    {
        var eventHub = new ServiceEventHub(typeof(ISampleInterface));
        var typedHub = new TypedEventHub<ISampleInterface>(eventHub);

        Assert.Same(eventHub, typedHub.Hub);
        Assert.Same(
            eventHub[nameof(ISampleInterface.SimpleEvent)],
            typedHub[nameof(ISampleInterface.SimpleEvent)]);
    }

    [Fact]
    public void TypedEventHub_created_from_hub_shares_state_with_it()
    {
        var eventHub = new ServiceEventHub(typeof(ISampleInterface));
        var typedHub = eventHub.CreateTyped<ISampleInterface>();

        Assert.Same(eventHub, typedHub.Hub);

        // adding via the typed hub is visible via the original hub
        var key = Guid.NewGuid();
        var handler = new EventHandler((s, e) => { });
        typedHub.AddHandler(nameof(ISampleInterface.SimpleEvent), key, Guid.Empty, handler);

        Assert.Equal(1, eventHub.GetHandlerCount(nameof(ISampleInterface.SimpleEvent)));

        // removing via the original hub is visible via the typed hub
        eventHub.RemoveHandler(nameof(ISampleInterface.SimpleEvent), key);
        Assert.Equal(0, eventHub.GetHandlerCount(nameof(ISampleInterface.SimpleEvent)));
    }

    [Fact]
    public async Task TypedEventHub_invokes_events_on_wrapped_hub()
    {
        var eventHub = new ServiceEventHub(typeof(ISampleInterface));
        var typedHub = eventHub.CreateTyped<ISampleInterface>();

        var fired = new AsyncCounter();
        typedHub.AddHandler(
            nameof(ISampleInterface.SimpleEvent),
            Guid.NewGuid(),
            Guid.Empty,
            new EventHandler((s, e) => fired++));

        typedHub.Invoke(nameof(ISampleInterface.SimpleEvent), this, EventArgs.Empty);
        Assert.Equal(1, await fired.WaitForValue(1).Timeout(1));

        // now wire the original hub to a component and fire events through it
        var component = new SampleService();
        eventHub.WireTo(component);
        component.FireHandlers(0);
        Assert.Equal(2, await fired.WaitForValue(2).Timeout(1));
    }

    [Fact]
    public void TypedEventHub_rejects_incompatible_hub()
    {
        var eventHub = new ServiceEventHub(typeof(ISampleInterface));

        Assert.Throws<ArgumentException>(() =>
            new TypedEventHub<ISampleUnrelated>(eventHub));
    }

    [Fact]
    public void TypedEventHub_accepts_hub_of_derived_interface()
    {
        // hub built for a descendant interface, wrapper for the ancestor
        var eventHub = new ServiceEventHub(typeof(ISampleDescendant2));
        var typedHub = new TypedEventHub<ISampleInterface>(eventHub);

        Assert.Same(eventHub, typedHub.Hub);
        Assert.NotNull(typedHub[nameof(ISampleInterface.SimpleEvent)]);
    }

    [Fact]
    public void TypedEventHub_treats_null_hub_as_fresh_hub()
    {
        var typedHub1 = new TypedEventHub<ISampleInterface>();
        var typedHub2 = new TypedEventHub<ISampleInterface>(null);

        Assert.NotNull(typedHub1.Hub);
        Assert.NotNull(typedHub2.Hub);
        Assert.NotSame(typedHub1.Hub, typedHub2.Hub);
        Assert.Equal(typeof(ISampleInterface), typedHub2.Hub.InterfaceType);
    }
}

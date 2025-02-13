using System;
using System.ComponentModel;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.Toolbox;
using Xunit;

namespace CoreRemoting.Tests;

public class EventStubTests
{
    public interface ISampleInterface
    {
        string FireHandlers(int argument);

        event EventHandler SimpleEvent;

        event EventHandler<CancelEventArgs> CancelEvent;

        Action ActionDelegate { get; set; }

        Func<int, string> FuncDelegate { get; set; }

        int SimpleEventHandlerCount { get; }
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

        public int SimpleEventHandlerCount
        {
            get { return EventStub.GetHandlerCount(SimpleEvent); }
        }
    }

    [Fact]
    public void EventStub_Contains_Events_And_Delegates()
    {
        var eventStub = new EventStub(typeof(ISampleInterface));
        Assert.NotNull(eventStub[nameof(ISampleInterface.SimpleEvent)]);
        Assert.NotNull(eventStub[nameof(ISampleInterface.CancelEvent)]);
        Assert.NotNull(eventStub[nameof(ISampleInterface.ActionDelegate)]);
        Assert.NotNull(eventStub[nameof(ISampleInterface.FuncDelegate)]);
    }

    [Fact]
    public void EventStub_Contains_Inherited_Events_And_Delegates()
    {
        var eventStub = new EventStub(typeof(ISampleDescendant2));
        Assert.NotNull(eventStub[nameof(ISampleDescendant2.NewCancelEvent)]);
        Assert.NotNull(eventStub[nameof(ISampleDescendant2.NewEvent)]);
        Assert.NotNull(eventStub[nameof(ISampleDescendant2.NewDelegate)]);
        Assert.NotNull(eventStub[nameof(ISampleDescendant2.SimpleEvent)]);
        Assert.NotNull(eventStub[nameof(ISampleDescendant2.CancelEvent)]);
        Assert.NotNull(eventStub[nameof(ISampleDescendant2.ActionDelegate)]);
        Assert.NotNull(eventStub[nameof(ISampleDescendant2.FuncDelegate)]);
    }

    [Fact]
    public void EventStub_Delegates_Have_Same_Types_As_Their_Originals()
    {
        var eventStub = new EventStub(typeof(ISampleInterface));
        Assert.IsType<EventHandler>(eventStub[nameof(ISampleInterface.SimpleEvent)]);
        Assert.IsType<EventHandler<CancelEventArgs>>(eventStub[nameof(ISampleInterface.CancelEvent)]);
        Assert.IsType<Action>(eventStub[nameof(ISampleInterface.ActionDelegate)]);
        Assert.IsType<Func<int, string>>(eventStub[nameof(ISampleInterface.FuncDelegate)]);
    }

    [Fact]
    public void EventStub_Simple_Handle_Tests()
    {
        // add the first handler
        var eventStub = new EventStub(typeof(ISampleInterface));
        var fired = false;
        eventStub.AddHandler(nameof(ISampleInterface.SimpleEvent), new EventHandler((sender, args) => fired = true));

        // check if it is called
        var handler = (EventHandler)eventStub[nameof(ISampleInterface.SimpleEvent)];
        handler(this, EventArgs.Empty);
        Assert.True(fired);

        // add the second handler
        fired = false;
        var firedAgain = false;
        var tempHandler = new EventHandler((sender, args) => firedAgain = true);
        eventStub.AddHandler(nameof(ISampleInterface.SimpleEvent), tempHandler);

        // check if it is called
        handler(this, EventArgs.Empty);
        Assert.True(fired);
        Assert.True(firedAgain);

        // remove the second handler
        fired = false;
        firedAgain = false;
        eventStub.RemoveHandler(nameof(ISampleInterface.SimpleEvent), tempHandler);

        // check if it is not called
        handler(this, EventArgs.Empty);
        Assert.True(fired);
        Assert.False(firedAgain);
    }

    [Fact]
    public void EventStub_Cancel_Event_Tests()
    {
        // add the first handler
        var eventStub = new EventStub(typeof(ISampleInterface));
        var fired = false;
        eventStub.AddHandler(nameof(ISampleInterface.CancelEvent), new EventHandler<CancelEventArgs>((sender, args) => fired = true));

        // check if it is called
        var handler = (EventHandler<CancelEventArgs>)eventStub[nameof(ISampleInterface.CancelEvent)];
        handler(this, new CancelEventArgs());
        Assert.True(fired);

        // add the second handler
        fired = false;
        var firedAgain = false;
        var tempHandler = new EventHandler<CancelEventArgs>((sender, args) => firedAgain = true);
        eventStub.AddHandler(nameof(ISampleInterface.CancelEvent), tempHandler);

        // check if it is called
        handler(this, new CancelEventArgs());
        Assert.True(fired);
        Assert.True(firedAgain);

        // remove the second handler
        fired = false;
        firedAgain = false;
        eventStub.RemoveHandler(nameof(ISampleInterface.CancelEvent), tempHandler);

        // check if it is not called
        handler(this, new CancelEventArgs());
        Assert.True(fired);
        Assert.False(firedAgain);
    }

    [Fact]
    public void EventStub_ActionDelegateTests()
    {
        // add the first handler
        var eventStub = new EventStub(typeof(ISampleInterface));
        var fired = false;
        eventStub.AddHandler(nameof(ISampleInterface.ActionDelegate), new Action(() => fired = true));

        // check if it is called
        var handler = (Action)eventStub[nameof(ISampleInterface.ActionDelegate)];
        handler();
        Assert.True(fired);

        // add the second handler
        fired = false;
        var firedAgain = false;
        var tempHandler = new Action(() => firedAgain = true);
        eventStub.AddHandler(nameof(ISampleInterface.ActionDelegate), tempHandler);

        // check if it is called
        handler();
        Assert.True(fired);
        Assert.True(firedAgain);

        // remove the second handler
        fired = false;
        firedAgain = false;
        eventStub.RemoveHandler(nameof(ISampleInterface.ActionDelegate), tempHandler);

        // check if it is not called
        handler();
        Assert.True(fired);
        Assert.False(firedAgain);
    }

    [Fact]
    public void EventStub_FuncDelegateTests()
    {
        // add the first handler
        var eventStub = new EventStub(typeof(ISampleInterface));
        var fired = false;
        eventStub.AddHandler(nameof(ISampleInterface.FuncDelegate), new Func<int, string>(a =>
        {
            fired = true;
            return a.ToString();
        }));

        // check if it is called
        var handler = (Func<int, string>)eventStub[nameof(ISampleInterface.FuncDelegate)];
        var result = handler(123);
        Assert.True(fired);
        Assert.Null(result); // Assert.Equal("123", result);

        // add the second handler
        fired = false;
        var firedAgain = false;
        var tempHandler = new Func<int, string>(a => { firedAgain = true; return a.ToString(); });
        eventStub.AddHandler(nameof(ISampleInterface.FuncDelegate), tempHandler);

        // check if it is called
        result = handler(321);
        Assert.True(fired);
        Assert.True(firedAgain);
        Assert.Null(result); // Assert.Equal("321", result);

        // remove the second handler
        fired = false;
        firedAgain = false;
        eventStub.RemoveHandler(nameof(ISampleInterface.FuncDelegate), tempHandler);

        // check if it is not called
        result = handler(0);
        Assert.True(fired);
        Assert.False(firedAgain);
        Assert.Null(result); // Assert.Equal("0", result);
    }

    [Fact]
    public void EventStub_WireUnwireTests()
    {
        var eventStub = new EventStub(typeof(ISampleInterface));
        var simpleEventFired = false;
        var cancelEventFired = false;
        var actionFired = false;
        var funcFired = false;

        // add event handlers
        eventStub.AddHandler(nameof(ISampleInterface.SimpleEvent), new EventHandler((sender, args) => simpleEventFired = true));
        eventStub.AddHandler(nameof(ISampleInterface.CancelEvent), new EventHandler<CancelEventArgs>((sender, args) => cancelEventFired = true));
        eventStub.AddHandler(nameof(ISampleInterface.ActionDelegate), new Action(() => actionFired = true));
        eventStub.AddHandler(nameof(ISampleInterface.FuncDelegate), new Func<int, string>(a => { funcFired = true; return a.ToString(); }));
        eventStub.AddHandler(nameof(ISampleInterface.FuncDelegate), new Func<int, string>(a => { return (a * 2).ToString(); }));

        // wire up events
        var component = new SampleService();
        eventStub.WireTo(component);

        // test if it works
        var result = component.FireHandlers(102030);
        Assert.Null(result); // Assert.Equal("204060", result);
        Assert.True(simpleEventFired);
        Assert.True(cancelEventFired);
        Assert.True(actionFired);
        Assert.True(funcFired);

        // unwire
        simpleEventFired = false;
        cancelEventFired = false;
        actionFired = false;
        funcFired = false;
        eventStub.UnwireFrom(component);

        // test if it works
        result = component.FireHandlers(123);
        Assert.Null(result);
        Assert.False(simpleEventFired);
        Assert.False(cancelEventFired);
        Assert.False(actionFired);
        Assert.False(funcFired);
    }

    [Fact]
    public void EventStub_Handler_Count_Tests()
    {
        var eventStub = new EventStub(typeof(ISampleInterface));
        var sampleService = new SampleService();

        eventStub.WireTo(sampleService);
        Assert.Equal(0, sampleService.SimpleEventHandlerCount);

        eventStub.AddHandler(nameof(ISampleInterface.SimpleEvent), new EventHandler((s, e) => { }));
        Assert.Equal(1, sampleService.SimpleEventHandlerCount);

        var handler = new EventHandler((s, e) => { });

        eventStub.AddHandler(nameof(ISampleInterface.SimpleEvent), handler);
        Assert.Equal(2, sampleService.SimpleEventHandlerCount);

        eventStub.RemoveHandler(nameof(ISampleInterface.SimpleEvent), handler);
        Assert.Equal(1, sampleService.SimpleEventHandlerCount);
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
}

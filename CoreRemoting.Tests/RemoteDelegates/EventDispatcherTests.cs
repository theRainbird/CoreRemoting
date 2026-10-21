using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Null;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.Tests.Tools;
using CoreRemoting.Threading;
using CoreRemoting.Toolbox;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests.RemoteDelegates;

public partial class EventDispatcherTests : RpcTests_Base
{
    public EventDispatcherTests(ServerFixture serverFixture, ITestOutputHelper testOutputHelper)
        : base(serverFixture, testOutputHelper)
    {
    }

    protected override IServerChannel ServerChannel => new NullServerChannel();

    protected override IClientChannel ClientChannel => new NullClientChannel();

    // Used to test the "unregistered service" path. Not registered anywhere.
    public interface INotRegisteredService
    {
        event Action SomeEvent;
    }

    private IEventDispatcher GetDispatcher() =>
        _serverFixture.Server.ServiceRegistry.GetService<IEventDispatcher>();

    private RemotingClient CreateConnectedClient()
    {
        var client = new RemotingClient(new ClientConfig
        {
            ConnectionTimeout = 0,
            InvocationTimeout = 0,
            SendTimeout = 0,
            Channel = ClientChannel,
            MessageEncryption = false,
            ServerPort = _serverFixture.Server.Config.NetworkPort,
        });

        client.Connect();
        return client;
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Dispatcher_invokes_event_via_generic_overload()
    {
        using var ctx = ValidationSyncContext.Install();

        using var client = CreateConnectedClient();
        var proxy = client.CreateProxy<ITestService>();

        var fired = new AsyncCounter();
        proxy.ServiceEvent += () => fired++;

        // Ensure the service and its event hub are materialized on the server.
        proxy.Echo("warmup");

        GetDispatcher().Invoke<ITestService>(s => nameof(s.ServiceEvent));

        await fired.WaitForValue(1).Timeout(1).ConfigureAwait(false);
        CheckServerErrorCount();
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Dispatcher_invokes_event_via_string_overload()
    {
        using var ctx = ValidationSyncContext.Install();

        using var client = CreateConnectedClient();
        var proxy = client.CreateProxy<ITestService>();

        var fired = new AsyncCounter();
        proxy.ServiceEvent += () => fired++;

        proxy.Echo("warmup");

        GetDispatcher().Invoke(
            typeof(ITestService).FullName,
            nameof(ITestService.ServiceEvent));

        await fired.WaitForValue(1).Timeout(1).ConfigureAwait(false);
        CheckServerErrorCount();
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Dispatcher_passes_arguments_to_event_handlers()
    {
        using var ctx = ValidationSyncContext.Install();

        using var client = CreateConnectedClient();
        var proxy = client.CreateProxy<ITestService>();

        object received = null;
        var fired = new AsyncCounter();

        proxy.CustomDelegateEvent += sender =>
        {
            received = sender;
            fired++;
        };

        proxy.Echo("warmup");

        GetDispatcher().Invoke<ITestService>(
            s => nameof(s.CustomDelegateEvent), "hello");

        await fired.WaitForValue(1).Timeout(1).ConfigureAwait(false);
        Assert.Equal("hello", received);
        CheckServerErrorCount();
    }

    [Theory]
    [InlineData("TestService_Singleton_Service")]
    [InlineData("TestService_Singleton_Factory")]
    [InlineData("TestService_SingleCall_Service")]
    [InlineData("TestService_SingleCall_Factory")]
    [InlineData("TestService_Scoped_Service")]
    [InlineData("TestService_Scoped_Factory")]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Dispatcher_can_invoke_events_for_any_service_lifetime(string serviceName)
    {
        using var ctx = ValidationSyncContext.Install();

        using var client = CreateConnectedClient();
        var proxy = client.CreateProxy<ITestService>(serviceName);

        var fired = new AsyncCounter();
        proxy.ServiceEvent += () => fired++;

        proxy.Echo("warmup");

        GetDispatcher().Invoke(serviceName, nameof(ITestService.ServiceEvent));

        await fired.WaitForValue(1).Timeout(1).ConfigureAwait(false);
        CheckServerErrorCount();
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Dispatcher_doesnt_throw_if_there_are_no_subscribers()
    {
        using var ctx = ValidationSyncContext.Install();

        using var client = CreateConnectedClient();
        var proxy = client.CreateProxy<ITestService>();

        // No subscribers. Warmup materializes the service and its event hub.
        proxy.Echo("warmup");

        GetDispatcher().Invoke<ITestService>(s => nameof(s.ServiceEvent));

        // Give the server a moment to settle
        await Task.Delay(100).ConfigureAwait(false);

        CheckServerErrorCount();
    }

    [Fact]
    public void Dispatcher_throws_for_unregistered_service()
    {
        using var ctx = ValidationSyncContext.Install();

        using var client = CreateConnectedClient();

        var dispatcher = GetDispatcher();

        Assert.Throws<KeyNotFoundException>(() =>
            dispatcher.Invoke<INotRegisteredService>(s => nameof(s.SomeEvent)));
    }

    [Fact]
    public void Dispatcher_throws_for_unregistered_event()
    {
        using var ctx = ValidationSyncContext.Install();

        using var client = CreateConnectedClient();
        var proxy = client.CreateProxy<ITestService>();

        // Subscribe and warmup so the event hub definitely exists on the server.
        proxy.ServiceEvent += () => { };
        proxy.Echo("warmup");

        var dispatcher = GetDispatcher();

        Assert.Throws<KeyNotFoundException>(() =>
            dispatcher.Invoke<ITestService>(s => "NoSuchEvent"));
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Service_can_inject_dispatcher_and_relay_another_service_event()
    {
        using var ctx = ValidationSyncContext.Install();

        using var client = CreateConnectedClient();
        var testProxy = client.CreateProxy<ITestService>();
        var relayProxy = client.CreateProxy<IEventRelay>();

        var fired = new AsyncCounter();
        testProxy.ServiceEvent += () => fired++;

        // Materialize the target service on the server before relaying its event.
        testProxy.Echo("warmup");

        relayProxy.RelayTestServiceEvent();

        await fired.WaitForValue(1).Timeout(1).ConfigureAwait(false);
        CheckServerErrorCount();
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait(false) in test method", Justification = "<Pending>")]
    public async Task Service_can_inject_dispatcher_and_relay_event_by_name()
    {
        using var ctx = ValidationSyncContext.Install();

        using var client = CreateConnectedClient();
        var testProxy = client.CreateProxy<ITestService>();
        var relayProxy = client.CreateProxy<IEventRelay>();

        var fired = new AsyncCounter();
        testProxy.ServiceEvent += () => fired++;

        testProxy.Echo("warmup");

        relayProxy.RelayTestServiceEventByName();

        await fired.WaitForValue(1).Timeout(1).ConfigureAwait(false);
        CheckServerErrorCount();
    }
}
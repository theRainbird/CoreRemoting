using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Serialization;
using CoreRemoting.Tests.Tools;
using CoreRemoting.Threading;
using CoreRemoting.Toolbox;
using Xunit;

namespace CoreRemoting.Tests;

public partial class RpcTests
{
    internal interface IInternalEchoService
    {
        string Echo(string text);

        int Sum(params int[] values);
    }

    private class InternalEchoService : IInternalEchoService
    {
        public string Echo(string text) => text;

        public int Sum(params int[] values) =>
            values?.Sum() ?? 0;
    }

    private interface IPrivateEchoService
    {
        string Echo(string text);

        int Sum(params int[] values);
    }

    private class PrivateEchoService : IPrivateEchoService
    {
        public string Echo(string text) => text;

        public int Sum(params int[] values) =>
            values?.Sum() ?? 0;
    }

    [Fact]
    public void CreateProxy_doesnt_work_with_internal_interface()
    {
        using var client = CreateConnectedClient();

        // Castle DynamicProxy cannot generate a proxy for an internal interface
        // unless the declaring assembly has InternalsVisibleTo("DynamicProxyGenAssembly2").
        // ServiceInvoker exists precisely to bypass this limitation.
        var ex = Assert.Throws<ArgumentException>(() => client.CreateProxy<IInternalEchoService>());
        Assert.Contains("not accessible", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateProxy_doesnt_work_with_private_interface()
    {
        using var client = CreateConnectedClient();

        // Castle DynamicProxy cannot generate a proxy for a private nested interface:
        // the generated proxy assembly has no way to reference it, and InternalsVisibleTo
        // doesn't help because the type is not even internal.
        var ex = Assert.Throws<ArgumentException>(() => client.CreateProxy<IPrivateEchoService>());
        Assert.Contains("not accessible", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServiceInvoker_works_with_internal_interface()
    {
        _serverFixture.Server.ServiceRegistry.RegisterService<IInternalEchoService>(
            () => new InternalEchoService(),
            ServiceLifetime.Singleton);

        using var client = CreateConnectedClient();

        using var invoker = client.CreateInvoker<IInternalEchoService>();

        Assert.Equal("test", invoker.Invoke(s => s.Echo("test")));
        Assert.Equal(6, invoker.Invoke(s => s.Sum(1, 2, 3)));
        Assert.Equal(0, invoker.Invoke(s => s.Sum()));

        CheckServerErrorCount();
    }

    [Fact]
    public void ServiceInvoker_works_with_private_interface()
    {
        _serverFixture.Server.ServiceRegistry.RegisterService<IPrivateEchoService>(
            () => new PrivateEchoService(),
            ServiceLifetime.Singleton);

        using var client = CreateConnectedClient();

        using var invoker = client.CreateInvoker<IPrivateEchoService>();

        Assert.Equal("test", invoker.Invoke(s => s.Echo("test")));
        Assert.Equal(6, invoker.Invoke(s => s.Sum(1, 2, 3)));
        Assert.Equal(0, invoker.Invoke(s => s.Sum()));

        CheckServerErrorCount();
    }

    [Fact]
    public void ServiceInvoker_throws_ObjectDisposedException_after_Dispose()
    {
        using var client = CreateConnectedClient();

        var invoker = client.CreateInvoker<ITestService>();
        invoker.Dispose();

        Assert.Throws<ObjectDisposedException>(() => invoker.Invoke(s => s.Echo("test")));
    }

    [Fact]
    public void ServiceInvoker_propagates_remote_exception()
    {
        using var client = CreateConnectedClient();
        using var invoker = client.CreateInvoker<ITestService>();

        try
        {
            var ex = Assert.Throws<RemoteInvocationException>(() =>
                invoker.Invoke(s => s.Error("boom")));

            Assert.Equal("boom", ex.GetInnermostException().Message);
        }
        finally
        {
            _serverFixture.ServerErrorCount = 0;
        }
    }

    [Fact]
    public void ServiceInvoker_supports_oneway_methods()
    {
        using var client = CreateConnectedClient();
        using var invoker = client.CreateInvoker<ITestService>();

        invoker.Invoke(s => s.OneWayMethod());

        CheckServerErrorCount();
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task ServiceInvoker_supports_delegate_arguments()
    {
        using var ctx = ValidationSyncContext.Install();
        using var client = CreateConnectedClient();
        using var invoker = client.CreateInvoker<ITestService>();

        string received = null;
        var called = new AsyncCounter();

        Action<string> handler = arg =>
        {
            received = arg;
            called++;
        };

        invoker.Invoke(s => s.TestMethodWithDelegateArg(handler));

        await called.WaitForValue(1).Timeout(1).ConfigureAwait(false);

        Assert.Equal("test", received);
        CheckServerErrorCount();
    }

    [Theory]
    [InlineData("TestService_Singleton_Service")]
    [InlineData("TestService_Singleton_Factory")]
    [InlineData("TestService_SingleCall_Service")]
    [InlineData("TestService_SingleCall_Factory")]
    [InlineData("TestService_Scoped_Service")]
    [InlineData("TestService_Scoped_Factory")]
    public void ServiceInvoker_sync_value(string serviceName)
    {
        using var client = CreateConnectedClient();
        using var invoker = client.CreateInvoker<ITestService>(serviceName);

        var result = invoker.Invoke(s => s.Echo("test"));

        Assert.Equal("test", result);
        CheckServerErrorCount();
    }

    [Theory]
    [InlineData("TestService_Singleton_Service")]
    [InlineData("TestService_Singleton_Factory")]
    [InlineData("TestService_SingleCall_Service")]
    [InlineData("TestService_SingleCall_Factory")]
    [InlineData("TestService_Scoped_Service")]
    [InlineData("TestService_Scoped_Factory")]
    public void ServiceInvoker_sync_void(string serviceName)
    {
        using var client = CreateConnectedClient();
        using var invoker = client.CreateInvoker<ITestService>(serviceName);

        invoker.Invoke(s => s.SaveLastInstance());

        // same instance check will succeed only for singletons
        var result = invoker.Invoke(s => s.CheckLastSavedInstance());
        var isSingleton = serviceName.Contains("Singleton");
        Assert.Equal(isSingleton, result);

        CheckServerErrorCount();
    }

    [Theory]
    [InlineData("TestService_Singleton_Service")]
    [InlineData("TestService_Singleton_Factory")]
    [InlineData("TestService_SingleCall_Service")]
    [InlineData("TestService_SingleCall_Factory")]
    [InlineData("TestService_Scoped_Service")]
    [InlineData("TestService_Scoped_Factory")]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task ServiceInvoker_Task_of_T(string serviceName)
    {
        using var ctx = ValidationSyncContext.Install();
        using var client = CreateConnectedClient();
        using var invoker = client.CreateInvoker<ITestService>(serviceName);

        var result = await invoker.Invoke(s => s.EchoAsync("test")).ConfigureAwait(false);

        Assert.Equal("test", result);
        CheckServerErrorCount();
    }

    [Theory]
    [InlineData("TestService_Singleton_Service")]
    [InlineData("TestService_Singleton_Factory")]
    [InlineData("TestService_SingleCall_Service")]
    [InlineData("TestService_SingleCall_Factory")]
    [InlineData("TestService_Scoped_Service")]
    [InlineData("TestService_Scoped_Factory")]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task ServiceInvoker_ValueTask_of_T(string serviceName)
    {
        using var ctx = ValidationSyncContext.Install();
        using var client = CreateConnectedClient();
        using var invoker = client.CreateInvoker<ITestService>(serviceName);

        var result = await invoker.Invoke(s => s.EchoValueTaskAsync("test")).ConfigureAwait(false);

        Assert.Equal("test", result);
        CheckServerErrorCount();
    }

    [Theory]
    [InlineData("TestService_Singleton_Service")]
    [InlineData("TestService_Singleton_Factory")]
    [InlineData("TestService_SingleCall_Service")]
    [InlineData("TestService_SingleCall_Factory")]
    [InlineData("TestService_Scoped_Service")]
    [InlineData("TestService_Scoped_Factory")]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task ServiceInvoker_Task(string serviceName)
    {
        using var ctx = ValidationSyncContext.Install();
        using var client = CreateConnectedClient();
        using var invoker = client.CreateInvoker<ITestService>(serviceName);

        await invoker.Invoke(s => s.NoopAsync()).ConfigureAwait(false);

        CheckServerErrorCount();
    }

    private RemotingClient CreateClient() =>
        new(new ClientConfig()
        {
            ConnectionTimeout = 0,
            InvocationTimeout = 0,
            SendTimeout = 0,
            Channel = ClientChannel,
            MessageEncryption = false,
            ServerPort = _serverFixture.Server.Config.NetworkPort,
        });

    private RemotingClient CreateConnectedClient()
    {
        var client = CreateClient();
        client.Connect();
        return client;
    }
}

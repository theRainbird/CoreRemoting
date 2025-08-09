using System.Diagnostics.CodeAnalysis;
using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests;

[Collection("CoreRemoting")]
public class AsyncTests : IClassFixture<ServerFixture>
{
    private ServerFixture _serverFixture;

    public AsyncTests(ServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        _serverFixture.Start();
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async void AsyncMethods_should_work()
    {
        using var client = new RemotingClient(new ClientConfig()
        {
            ConnectionTimeout = 0,
            InvocationTimeout = 0,
            MessageEncryption = false,
            ServerPort = _serverFixture.Server.Config.NetworkPort
        });

        client.Connect();
        var proxy = client.CreateProxy<IAsyncService>();

        var base64String = await proxy.ConvertToBase64Async("Yay");

        Assert.Equal("WWF5", base64String);
    }

    /// <summary>
    /// Awaiting for ordinary non-generic task method should not hang.
    /// </summary>
    [Fact(Timeout = 15000)]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async void Awaiting_non_generic_Task_should_not_hang_forever()
    {
        using var client = new RemotingClient(new ClientConfig()
        {
            ConnectionTimeout = 0,
            InvocationTimeout = 0,
            MessageEncryption = false,
            ServerPort = _serverFixture.Server.Config.NetworkPort
        });

        client.Connect();
        var proxy = client.CreateProxy<IAsyncService>();

        await proxy.NonGenericTask();
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async void AsyncMethods_returning_ValueTask_should_work()
    {
        using var client = new RemotingClient(new ClientConfig()
        {
            ConnectionTimeout = 0,
            InvocationTimeout = 0,
            MessageEncryption = false,
            ServerPort = _serverFixture.Server.Config.NetworkPort
        });

        client.Connect();
        var proxy = client.CreateProxy<IAsyncService>();

        var base64String = await proxy.ConvertToBase64ValueTaskAsync("ValueTask");

        Assert.Equal("VmFsdWVUYXNr", base64String);
    }

    /// <summary>
    /// Awaiting for ordinary non-generic ValueTask method should not hang.
    /// </summary>
    [Fact(Timeout = 15000)]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async void Awaiting_non_generic_ValueTask_should_work()
    {
        using var client = new RemotingClient(new ClientConfig()
        {
            ConnectionTimeout = 0,
            InvocationTimeout = 0,
            MessageEncryption = false,
            ServerPort = _serverFixture.Server.Config.NetworkPort
        });

        client.Connect();
        var proxy = client.CreateProxy<IAsyncService>();

        await proxy.NonGenericValueTask();
    }
}
using System;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.Channels;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests;

[Collection("CoreRemoting")]
public abstract class RpcTests_Base : IClassFixture<ServerFixture>
{
    protected readonly ServerFixture _serverFixture;
    protected readonly ITestOutputHelper _testOutputHelper;
    protected bool _remoteServiceCalled;

    protected virtual IServerChannel ServerChannel => null;

    protected virtual IClientChannel ClientChannel => null;

    protected RpcTests_Base(ServerFixture serverFixture, ITestOutputHelper testOutputHelper)
    {
        _serverFixture = serverFixture;
        _testOutputHelper = testOutputHelper;

        ConfigureServer(_serverFixture.ServerConfig);

        _serverFixture.TestService.TestMethodFake = arg =>
        {
            _remoteServiceCalled = true;
            return arg;
        };

        _serverFixture.Start(ServerChannel);

        EventStub.DelegateInvoker = DelegateInvoker;
    }

    /// <summary>
    /// Gives derived tests a chance to configure the server before it starts.
    /// </summary>
    /// <param name="config">Server configuration object that will be used to create the server.</param>
    protected virtual void ConfigureServer(ServerConfig config)
    {
    }

    /// <summary>
    /// Gets the delegate invoker to be used for event handler tests.
    /// </summary>
    protected virtual IDelegateInvoker DelegateInvoker => new SafeDynamicInvoker();

    protected void CheckServerErrorCount()
    {
        try
        {
            Assert.Equal(0, _serverFixture.ServerErrorCount);
        }
        finally
        {
            if (_serverFixture.ServerErrorCount != 0)
            {
                var msg = $"LastServerError: {_serverFixture.LastServerError}";
                _testOutputHelper.WriteLine(msg);
                Console.WriteLine(msg);
            }
        }
    }
}

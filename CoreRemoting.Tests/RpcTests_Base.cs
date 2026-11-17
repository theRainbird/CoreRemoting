using System;
using System.Diagnostics;
using System.Threading.Tasks;
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

        ServiceEventHub.DelegateInvoker = DelegateInvoker;
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

    /// <summary>
    /// Waits until the given condition becomes true, or throws <see cref="TimeoutException"/>.
    /// Intended for asserting asynchronous server-side effects that occur after
    /// a client call has already returned.
    /// </summary>
    /// <param name="condition">Condition to poll.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    protected static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();

        while (!condition())
        {
            if (stopwatch.Elapsed > timeout)
                throw new TimeoutException();

            await Task.Delay(20).ConfigureAwait(false);
        }
    }
}

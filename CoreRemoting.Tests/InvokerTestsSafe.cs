using CoreRemoting.RemoteDelegates;
using CoreRemoting.Threading;
using Xunit.Abstractions;

namespace CoreRemoting.Tests;

public class InvokerTestsSafe : InvokerTests
{
    public InvokerTestsSafe(ServerFixture serverFixture, ITestOutputHelper testOutputHelper) : base(serverFixture, testOutputHelper)
    {
    }

    protected override IDelegateInvoker DelegateInvoker => new SafeDynamicInvoker();

    protected override bool ShouldPreserveEventOrder => false;
}
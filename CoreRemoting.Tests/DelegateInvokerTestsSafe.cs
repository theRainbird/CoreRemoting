using CoreRemoting.RemoteDelegates;
using Xunit.Abstractions;

namespace CoreRemoting.Tests;

public class DelegateInvokerTestsSafe : DelegateInvokerTests
{
    public DelegateInvokerTestsSafe(ServerFixture serverFixture, ITestOutputHelper testOutputHelper) : base(serverFixture, testOutputHelper)
    {
    }

    protected override IDelegateInvoker DelegateInvoker => new SafeDynamicInvoker();

    protected override bool ShouldPreserveEventOrder => false;
}
using CoreRemoting.RemoteDelegates;
using Xunit.Abstractions;

namespace CoreRemoting.Tests;

public class RpcTests_NullChannel_SimpleInvoker : RpcTests_NullChannel
{
    public RpcTests_NullChannel_SimpleInvoker(ServerFixture fixture, ITestOutputHelper helper) : base(fixture, helper)
    {
    }

	protected override IDelegateInvoker DelegateInvoker => new SimpleDynamicInvoker();
}
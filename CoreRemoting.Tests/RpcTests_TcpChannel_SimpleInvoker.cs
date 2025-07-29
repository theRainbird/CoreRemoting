using CoreRemoting.RemoteDelegates;
using Xunit.Abstractions;

namespace CoreRemoting.Tests;

public class RpcTests_TcpChannel_SimpleInvoker : RpcTests
{
    public RpcTests_TcpChannel_SimpleInvoker(ServerFixture fixture, ITestOutputHelper helper) : base(fixture, helper)
    {
    }

	protected override IDelegateInvoker DelegateInvoker => new SimpleDynamicInvoker();
}
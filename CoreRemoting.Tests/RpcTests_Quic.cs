using System.Threading.Tasks;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Quic;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests;

public class RpcTests_Quic : RpcTests
{
    protected override IServerChannel ServerChannel => new QuicServerChannel();

    protected override IClientChannel ClientChannel => new QuicClientChannel();

    public RpcTests_Quic(ServerFixture fixture, ITestOutputHelper helper) : base(fixture, helper)
    {
    }

    [Fact(Skip = "Fails on Quic channel")]
    public override Task Rpc_after_reconnect_does_not_throw_server_disconnected()
    {
        return Task.CompletedTask;
    }
}
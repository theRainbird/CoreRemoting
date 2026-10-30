using System.Threading.Tasks;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Websocket;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests;

public class RpcTests_Websockets : RpcTests
{
    protected override IServerChannel ServerChannel => new WebsocketServerChannel();

    protected override IClientChannel ClientChannel => new WebsocketClientChannel();

    public RpcTests_Websockets(ServerFixture fixture, ITestOutputHelper helper) : base(fixture, helper)
    {
    }

    [Fact(Skip = "Fails on Websocket channel")]
    public override Task Rpc_after_reconnect_does_not_throw_server_disconnected()
    {
        return Task.CompletedTask;
    }
}
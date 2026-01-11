using CoreRemoting.Channels;
using CoreRemoting.Channels.Websocket;
using Xunit.Abstractions;

namespace CoreRemoting.Tests;

public class RpcTests_Websockets : RpcTests
{
    protected override IServerChannel ServerChannel => new WebsocketServerChannel();

    protected override IClientChannel ClientChannel => new WebsocketClientChannel();

    public RpcTests_Websockets(ServerFixture fixture, ITestOutputHelper helper) : base(fixture, helper)
    {
    }
}
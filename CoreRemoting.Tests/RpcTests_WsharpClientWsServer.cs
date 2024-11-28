using CoreRemoting.Channels;
using CoreRemoting.Channels.Websocket;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests
{
    public class RpcTests_WsharpClientWsServer : RpcTests
    {
        protected override IServerChannel ServerChannel => new WebsocketServerChannel();

        protected override IClientChannel ClientChannel => new WebsocketSharpClientChannel();

        public RpcTests_WsharpClientWsServer(ServerFixture fixture, ITestOutputHelper helper) : base(fixture, helper)
        {
        }
    }
}
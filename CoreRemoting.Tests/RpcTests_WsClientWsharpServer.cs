using CoreRemoting.Channels;
using CoreRemoting.Channels.Websocket;
using CoreRemoting.Channels.WebsocketSharp;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests
{
    public class RpcTests_WsClientWsharpServer : RpcTests
    {
        protected override IServerChannel ServerChannel => new WebsocketSharpServerChannel();

        protected override IClientChannel ClientChannel => new WebsocketClientChannel();

        public RpcTests_WsClientWsharpServer(ServerFixture fixture, ITestOutputHelper helper) : base(fixture, helper)
        {
        }
    }
}
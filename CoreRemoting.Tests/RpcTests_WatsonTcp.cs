using CoreRemoting.Channels;
using CoreRemoting.Channels.Tcp;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests
{
    public class RpcTests_WatsonTcp : RpcTests
    {
        protected override IServerChannel ServerChannel => new TcpServerChannel();

        protected override IClientChannel ClientChannel => new TcpClientChannel();

        public RpcTests_WatsonTcp(ServerFixture s, ITestOutputHelper h) : base(s, h)
        {
        }
    }
}
using CoreRemoting.Channels;
using CoreRemoting.Channels.Quic;
using Xunit.Abstractions;

namespace CoreRemoting.Tests
{
    public class RpcTests_Quic : RpcTests
    {
        protected override IServerChannel ServerChannel => new QuicServerChannel();

        protected override IClientChannel ClientChannel => new QuicClientChannel();

        public RpcTests_Quic(ServerFixture fixture, ITestOutputHelper helper) : base(fixture, helper)
        {
        }
    }
}
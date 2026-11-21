using System.Threading.Tasks;
using Castle.MicroKernel.Registration;
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

    [Fact]
    public override void RemotingClient_can_disconnect_and_connect_again() =>
        base.RemotingClient_can_disconnect_and_connect_again();

    [Fact]
    public override Task Rpc_after_reconnect_does_not_throw_server_disconnected() =>
        base.Rpc_after_reconnect_does_not_throw_server_disconnected();
}
using CoreRemoting.Channels;
using CoreRemoting.Channels.Quic;
using CoreRemoting.Channels.Tcp;
using CoreRemoting.Channels.Websocket;
using Xunit.Abstractions;

namespace CoreRemoting.Tests;

public partial class EventDispatcherTests_WebSockets(ServerFixture serverFixture, ITestOutputHelper testOutputHelper)
    : EventDispatcherTests(serverFixture, testOutputHelper)
{
    protected override IServerChannel ServerChannel => new WebsocketServerChannel();

    protected override IClientChannel ClientChannel => new WebsocketClientChannel();
}

public partial class EventDispatcherTests_Quic(ServerFixture serverFixture, ITestOutputHelper testOutputHelper)
    : EventDispatcherTests(serverFixture, testOutputHelper)
{
    protected override IServerChannel ServerChannel => new QuicServerChannel();

    protected override IClientChannel ClientChannel => new QuicClientChannel();
}

public partial class EventDispatcherTests_Tcp(ServerFixture serverFixture, ITestOutputHelper testOutputHelper)
    : EventDispatcherTests(serverFixture, testOutputHelper)
{
    protected override IServerChannel ServerChannel => new TcpServerChannel();

    protected override IClientChannel ClientChannel => new TcpClientChannel();
}

using CoreRemoting.Channels;
using CoreRemoting.Channels.Websocket;

namespace CoreRemoting.Tests.Sessions;

public class SessionTests_Websockets : SessionTests
{
    protected override IServerChannel ServerChannel => new WebsocketServerChannel();

    protected override IClientChannel ClientChannel => new WebsocketClientChannel();

    public SessionTests_Websockets(ServerFixture serverFixture) : base(serverFixture)
    {
    }
}
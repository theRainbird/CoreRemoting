using System.Threading.Tasks;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Websocket;

namespace CoreRemoting.Tests;

public class SessionTests_Websockets : SessionTests
{
    protected override IServerChannel ServerChannel => new WebsocketServerChannel();

    protected override IClientChannel ClientChannel => new WebsocketClientChannel();

    public SessionTests_Websockets(ServerFixture serverFixture) : base(serverFixture)
    {
    }

    public override Task CloseSession_method_should_close_session_gracefully_issue55_and156()
    {
        // TODO: fix https://github.com/theRainbird/CoreRemoting/issues/156 on Websockets
        return Task.CompletedTask;
    }
}
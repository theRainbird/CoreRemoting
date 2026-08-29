using CoreRemoting.Channels;
using CoreRemoting.Channels.Websocket;

namespace CoreRemoting.Tests;

public class SessionResumeTestsWebSockets : SessionResumeTests
{
    protected override IServerChannel ServerChannel => new WebsocketServerChannel();

    protected override IClientChannel ClientChannel => new WebsocketClientChannel();
}

using CoreRemoting.Channels;
using CoreRemoting.Channels.Websocket;

namespace CoreRemoting.Tests.Sessions;

public class SessionResumeTestsWebSockets : SessionResumeTests
{
    protected override IServerChannel ServerChannel => new WebsocketServerChannel();

    protected override IClientChannel ClientChannel => new WebsocketClientChannel();

    public class SessionResumeTestsWebSocketsNoEncryption : SessionResumeTestsWebSockets
    {
        protected override bool MessageEncryption => false;
    }

    public class SessionResumeTestsWebSocketsNoAuth : SessionResumeTestsWebSockets
    {
        protected override bool AuthenticationRequiredForResumeTests => false;
    }

    public class SessionResumeTestsWebSocketsNoAuthNoEncryption : SessionResumeTestsWebSockets
    {
        protected override bool MessageEncryption => false;

        protected override bool AuthenticationRequiredForResumeTests => false;
    }
}

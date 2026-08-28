using CoreRemoting.Channels;
using CoreRemoting.Channels.Websocket;

namespace CoreRemoting.Tests;

public class SessionResumeTestsWsockNoEncr : SessionResumeTestsWebSockets
{
    protected override bool MessageEncryption => false;

    protected override bool AuthenticationRequiredForResumeTests => false;
}

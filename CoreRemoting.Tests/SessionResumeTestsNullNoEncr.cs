using CoreRemoting.Channels;
using CoreRemoting.Channels.Null;

namespace CoreRemoting.Tests;

public class SessionResumeTestsNullNoEncr : SessionResumeTestsNullChannel
{
    protected override bool MessageEncryption => false;

    protected override bool AuthenticationRequiredForResumeTests => false;
}

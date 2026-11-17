using CoreRemoting.Channels;
using CoreRemoting.Channels.Null;

namespace CoreRemoting.Tests.Sessions;

public class SessionResumeTestsNullChannel : SessionResumeTests
{
    protected override IServerChannel ServerChannel => new NullServerChannel();

    protected override IClientChannel ClientChannel => new NullClientChannel();

    public class SessionResumeTestsNullNoEncryption : SessionResumeTestsNullChannel
    {
        protected override bool MessageEncryption => false;
    }

    public class SessionResumeTestsNullNoAuth : SessionResumeTestsNullChannel
    {
        protected override bool AuthenticationRequiredForResumeTests => false;
    }

    public class SessionResumeTestsNullNoAuthNoEncryption : SessionResumeTestsNullChannel
    {
        protected override bool MessageEncryption => false;

        protected override bool AuthenticationRequiredForResumeTests => false;
    }
}

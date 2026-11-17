namespace CoreRemoting.Tests.Sessions;

public class SessionResumeTestsNoEncryption : SessionResumeTests
{
    protected override bool MessageEncryption => false;

    public class SessionResumeTestsNoAuth : SessionResumeTests
    {
        protected override bool AuthenticationRequiredForResumeTests => false;
    }

    public class SessionResumeTestsNoAuthNoEncryption : SessionResumeTests
    {
        protected override bool MessageEncryption => false;

        protected override bool AuthenticationRequiredForResumeTests => false;
    }
}

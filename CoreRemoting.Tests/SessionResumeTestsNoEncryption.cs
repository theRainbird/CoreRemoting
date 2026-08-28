namespace CoreRemoting.Tests;

public class SessionResumeTestsNoEncryption : SessionResumeTests
{
    protected override bool MessageEncryption => false;

    protected override bool AuthenticationRequiredForResumeTests => false;
}

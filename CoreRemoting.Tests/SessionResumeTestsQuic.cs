using System;
using System.Net.Quic;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Quic;
using Xunit;

namespace CoreRemoting.Tests;

public class SessionResumeTestsQuic : SessionResumeTests
{
    protected override IServerChannel ServerChannel => new QuicServerChannel();

    protected override IClientChannel ClientChannel => new QuicClientChannel();

    protected override bool MessageEncryption => false;

    protected override bool AuthenticationRequiredForResumeTests => false;

    protected override void CheckServerErrorCount()
    {
        if (lastServerError is not null)
        {
            while (lastServerError.InnerException is Exception ex)
                lastServerError = ex;

            if (lastServerError is not QuicException)
                throw new Exception($"Unexpected server error: {lastServerError}");
        }

        // error 'Connection aborted by peer (12).' is expected
        Assert.True(serverErrorCount <= 2);
    }
}

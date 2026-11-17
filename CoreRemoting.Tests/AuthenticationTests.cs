using System.Linq;
using CoreRemoting.Authentication;
using CoreRemoting.Toolbox;
using Xunit;

namespace CoreRemoting.Tests;

public class AuthenticationTests
{
    [Fact]
    public void AuthenticationRequestMessageIndexerWorks()
    {
        var msg = new AuthenticationRequestMessage();
        var userName = msg["userName"];
        Assert.Null(userName);

        msg.Credentials = msg.Credentials.Append(new
        {
            userName = "user",
            password = "secret",
            salt = "pepper",
            protocol = "insecure-plaintext-password",
        });

        Assert.Equal("user", msg["userName"]);
        Assert.Equal("secret", msg["password"]);
        Assert.Equal("pepper", msg["salt"]);
        Assert.Equal("insecure-plaintext-password", msg["protocol"]);
    }
}


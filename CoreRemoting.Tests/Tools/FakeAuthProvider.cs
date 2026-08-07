using System;
using CoreRemoting.Authentication;

namespace CoreRemoting.Tests.Tools;

public class FakeAuthProvider : IAuthenticationProvider
{
    public Func<Credential[], bool> AuthenticateFake { get; set; }
    
    public AuthenticationResponseMessage Authenticate(AuthenticationRequestMessage request)
    {
        var success = AuthenticateFake?.Invoke(request.Credentials) ?? true;

        return new AuthenticationResponseMessage
        {
            IsAuthenticated = success,
            AuthenticatedIdentity = new RemotingIdentity()
            {
                AuthenticationType = "Fake",
                Domain = "domain",
                IsAuthenticated = success,
                Name = request.Credentials[0].Value,
                Roles = ["Test"],
            }
        };
    }
}
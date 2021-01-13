using System;
using CoreRemoting.Authentication;

namespace CoreRemoting.Tests.Tools
{
    public class FakeAuthProvider : IAuthenticationProvider
    {
        public Func<Credential[], bool> AuthenticateFake { get; set; }
        
        public bool Authenticate(Credential[] credentials, out RemotingIdentity authenticatedIdentity)
        {
            var success = AuthenticateFake(credentials);

            authenticatedIdentity =
                new RemotingIdentity()
                {
                    AuthenticationType = "Fake",
                    Domain = "domain",
                    IsAuthenticated = success,
                    Name = credentials[0].Value,
                    Roles = new[] {"Test"}
                };

            return success;
        }
    }
}
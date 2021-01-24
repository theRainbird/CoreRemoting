using System;
using System.Linq;
using System.DirectoryServices.AccountManagement;
using System.Security.Principal;

namespace CoreRemoting.Authentication
{
    public class WindowsAuthProvider : IAuthenticationProvider
    {
        public bool Authenticate(Credential[] credentials, out RemotingIdentity authenticatedIdentity)
        {
            authenticatedIdentity = null;

            if (credentials == null)
                return false;

            var domain =
                credentials
                    .Where(c => c.Name.ToLower() == "domain")
                    .Select(c => c.Value)
                    .FirstOrDefault();

            var userName =
                credentials
                    .Where(c => c.Name.ToLower() == "username")
                    .Select(c => c.Value)
                    .FirstOrDefault();

            var password =
                credentials
                    .Where(c => c.Name.ToLower() == "password")
                    .Select(c => c.Value)
                    .FirstOrDefault();

            PrincipalContext principalContext;
            string identityName;

            if (string.IsNullOrEmpty(domain))
            {
                principalContext = new PrincipalContext(ContextType.Machine);
                identityName = userName;
            }
            else
            {
                principalContext = new PrincipalContext(ContextType.Domain, domain);
                identityName = domain + "\\" + userName;                
            }
            
            var isAuthenticated = principalContext.ValidateCredentials(userName, password);

            if (isAuthenticated)
            {
                var principal = UserPrincipal.FindByIdentity(principalContext, identityName);

                // find all groups the user is member of (the check is recursive).
                // Guid != null check is intended to remove all built-in objects that are not really AD gorups.
                // the Sid.Translate method gets the DOMAIN\Group name format.
                var userIsMemberOf = principal.GetAuthorizationGroups().Select(group => group.Name);

                authenticatedIdentity =
                    new RemotingIdentity()
                    {
                        Name = identityName,
                        IsAuthenticated = true,
                        Roles = userIsMemberOf.ToArray()
                    };

                return true;
            }

            return false;
        }
    }
}

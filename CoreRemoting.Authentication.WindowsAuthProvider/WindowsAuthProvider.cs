using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.DirectoryServices.AccountManagement;

namespace CoreRemoting.Authentication
{
    /// <summary>
    /// Authentication provider to check credentials against Windows user accounts.
    /// </summary>
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public class WindowsAuthProvider : IAuthenticationProvider
    {
        public const string CREDENTIAL_TYPE_DOMAIN = "domain";
        public const string CREDENTIAL_TYPE_USERNAME = "username";
        public const string CREDENTIAL_TYPE_PASSWORD = "password";
        
        /// <summary>
        /// Authenticates the provided credentials and returns the authenticated identity, if successful.
        /// </summary>
        /// <param name="credentials">Array of credentials ("username", "password" and optional "domain")</param>
        /// <param name="authenticatedIdentity">Authenticated Identity</param>
        /// <returns>Indicates whether the authentication was successful.</returns>
        public bool Authenticate(Credential[] credentials, out RemotingIdentity authenticatedIdentity)
        {
            authenticatedIdentity = null;

            if (credentials == null)
                return false;

            var domain =
                credentials
                    .Where(c => c.Name.ToLower() == CREDENTIAL_TYPE_DOMAIN)
                    .Select(c => c.Value)
                    .FirstOrDefault();

            var userName =
                credentials
                    .Where(c => c.Name.ToLower() == CREDENTIAL_TYPE_USERNAME)
                    .Select(c => c.Value)
                    .FirstOrDefault();

            var password =
                credentials
                    .Where(c => c.Name.ToLower() == CREDENTIAL_TYPE_PASSWORD)
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
                var principal = UserPrincipal.FindByIdentity(principalContext, identityName ?? string.Empty);
                var userIsMemberOf = 
                    principal == null
                        ? new string[0]
                        : principal.GetAuthorizationGroups().Select(group => group.Name);

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

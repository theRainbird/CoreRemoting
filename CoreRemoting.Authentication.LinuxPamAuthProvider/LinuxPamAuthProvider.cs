using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Npam;

namespace CoreRemoting.Authentication;

/// <summary>
/// Authentication provider to check credentials against local Linux user accounts.
/// </summary>
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public class LinuxPamAuthProvider : IAuthenticationProvider
{
    public const string CREDENTIAL_TYPE_USERNAME = "username";
    public const string CREDENTIAL_TYPE_PASSWORD = "password";

    /// <summary>
    /// Authenticates the provided credentials and returns the authenticated identity, if successful.
    /// </summary>
    /// <param name="credentials">Array of credentials ("username", "password")</param>
    /// <param name="authenticatedIdentity">Authenticated Identity</param>
    /// <returns>Indicates whether the authentication was successful.</returns>
    public AuthenticationResponseMessage Authenticate(AuthenticationRequestMessage request)
    {
        var failed = new AuthenticationResponseMessage
        {
            IsAuthenticated = false,
            AuthenticatedIdentity = null,
        };

        if (request?.Credentials == null)
            return failed;

        var userName =
            request.Credentials
                .Where(c => c.Name.ToLower() == CREDENTIAL_TYPE_USERNAME)
                .Select(c => c.Value)
                .FirstOrDefault();
        
        var password =
            request.Credentials
                .Where(c => c.Name.ToLower() == CREDENTIAL_TYPE_PASSWORD)
                .Select(c => c.Value)
                .FirstOrDefault();

        var isAuthenticated = NpamUser.Authenticate("passwd", userName, password);

        if (isAuthenticated)
        {
            var accountInfo = NpamUser.GetAccountInfo(userName);

            return new AuthenticationResponseMessage
            {
                IsAuthenticated = true,
                AuthenticatedIdentity = new RemotingIdentity()
                {
                    Name = accountInfo.Username,
                    IsAuthenticated = true,
                    Roles = new[] { accountInfo.GroupID.ToString() }
                }
            };
        }

        return failed;
    }
}
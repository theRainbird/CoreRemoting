using System.Threading.Tasks;

namespace CoreRemoting.Authentication;

/// <summary>
/// Default authenticator that calls the remote authentication provider just once.
/// </summary>
public class DefaultAuthenticator : IAuthenticator
{
    /// <summary>
    /// Invokes the remote authentication procedure and returns its response.
    /// </summary>
    /// <param name="credentials">Credentials to authenticate.</param>
    /// <param name="remoteAuth">Server-side authentication provider.</param>
    /// <returns>The response returned by remote authentication provider.</returns>
    public async Task Authenticate(Credential[] credentials, IAuthenticationProvider remoteAuth)
    {
        if (credentials == null || credentials.Length == 0)
            return;

        await remoteAuth.Authenticate(new AuthenticationRequestMessage
        {
            Credentials = credentials,
        });
    }
}

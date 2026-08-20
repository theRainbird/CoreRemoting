using System.Threading.Tasks;

namespace CoreRemoting.Authentication;

/// <summary>
/// Interface for client-side authenticators.
/// </summary>
public interface IAuthenticator
{
    /// <summary>
    /// Authenticates the credentials by calling the remote authentication provider accorting to the selected protocol.
    /// </summary>
    /// <param name="credentials">Credentials.</param>
    /// <param name="authProxy">A proxy for the remote authentication provider.</param>
    /// <returns>The last authentication response sent by the remote provider.</returns>
    Task<AuthenticationResponseMessage> Authenticate(Credential[] credentials, IAuthenticationProvider authProxy);
}

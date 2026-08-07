namespace CoreRemoting.Authentication;

/// <summary>
/// Interface for authentication providers.
/// </summary>
public interface IAuthenticationProvider
{
    /// <summary>
    /// Authenticates the provided credentials and returns the response message containing the authenticated identity, if successful.
    /// </summary>
    /// <param name="request">Authentication request message.</param>
    /// <returns>Authentication response message including the authenticated identity.</returns>
    AuthenticationResponseMessage Authenticate(AuthenticationRequestMessage request);
}
namespace CoreRemoting.Authentication
{
    /// <summary>
    /// Interface for authentication providers.
    /// </summary>
    public interface IAuthenticationProvider
    {
        /// <summary>
        /// Authenticates the provided credentials and returns the authenticated identity, if successful.
        /// </summary>
        /// <param name="credentials">Array of credentials</param>
        /// <param name="authenticatedIdentity">Authenticated Identity</param>
        /// <returns>Indicates whether the authentication was successful.</returns>
        bool Authenticate(Credential[] credentials, out RemotingIdentity authenticatedIdentity);
    }
}
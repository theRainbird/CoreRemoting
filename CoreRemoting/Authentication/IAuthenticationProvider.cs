namespace CoreRemoting.Authentication
{
    public interface IAuthenticationProvider
    {
        bool Authenticate(Credential[] credentials, out RemotingIdentity authenticatedIdentity);
    }
}
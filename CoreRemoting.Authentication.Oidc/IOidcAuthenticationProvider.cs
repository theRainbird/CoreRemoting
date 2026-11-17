using CoreRemoting.Authentication;

namespace CoreRemoting.Authentication.Oidc;

/// <summary>
/// Server-side authentication provider that validates tokens issued by an external
/// OpenID Connect (OIDC) identity provider against its JWKS endpoint.
/// </summary>
/// <remarks>
/// Pattern A: the client sends a JWT in a credential named <see cref="OidcProtocolConstants.OIDC_TOKEN"/>;
/// the provider validates the signature, issuer, audience and lifetime, then populates
/// <see cref="CoreRemoting.Authentication.RemotingIdentity.Claims"/>.
/// Pattern B: if a step-up validator is configured, the provider completes only after the client
/// additionally provides a valid step-up factor (e.g., one-time password).
/// </remarks>
public interface IOidcAuthenticationProvider : IAuthenticationProvider
{
}

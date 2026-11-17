using System;
using System.Net.Http;

namespace CoreRemoting.Authentication.Oidc;

/// <summary>
/// Determines which token of the OIDC token response is returned by an <see cref="OidcTokenAcquirer"/>.
/// </summary>
public enum OidcTokenKind
{
    /// <summary>
    /// Returns the "id_token" (contains the "sub", "iss", "aud" and "exp" claims). This is the default and matches
    /// the validation performed by <see cref="OidcAuthenticationProvider"/>.
    /// </summary>
    IdToken,

    /// <summary>
    /// Returns the "access_token".
    /// </summary>
    AccessToken
}

/// <summary>
/// Configuration settings of an OIDC token requirer (client side) that performs the Authorization Code flow
/// with PKCE against an identity provider (e.g., Keycloak) and returns an OIDC token.
/// </summary>
public class OidcClientOptions
{
    /// <summary>
    /// Gets or sets the issuer URL of the identity provider (required).
    /// Must match the "iss" claim of the returned token and the issuer configured on the server side.
    /// The discovery document is fetched from "{Issuer}/.well-known/openid-configuration".
    /// </summary>
    public string Issuer { get; set; }

    /// <summary>
    /// Gets or sets the OAuth client id (required).
    /// </summary>
    public string ClientId { get; set; }

    /// <summary>
    /// Gets or sets the optional client secret. Leave null for a public client (Authorization Code flow with PKCE).
    /// </summary>
    public string ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the redirect URI. If null, a loopback redirect URI (http://127.0.0.1:<port>/) is used automatically.
    /// </summary>
    public string RedirectUri { get; set; }

    /// <summary>
    /// Gets or sets the OIDC scopes to request (default: "openid", "profile").
    /// The "openid" scope is required to obtain an id_token.
    /// </summary>
    public string[] Scopes { get; set; } = new[] { "openid", "profile" };

    /// <summary>
    /// Gets or sets which token of the token response should be returned (default: <see cref="OidcTokenKind.IdToken"/>).
    /// </summary>
    public OidcTokenKind TokenKind { get; set; } = OidcTokenKind.IdToken;

    /// <summary>
    /// Gets or sets an optional <see cref="HttpClient"/>. If null, an <see cref="HttpClient"/> is created internally.
    /// </summary>
    public HttpClient HttpClient { get; set; }

    /// <summary>
    /// Gets or sets an optional delegate used to open the browser for the authorization request.
    /// If null, an OS-specific default opener is used (falling back to printing the URL).
    /// </summary>
    public Action<Uri> BrowserOpener { get; set; }

    /// <summary>
    /// Gets or sets the maximum time to wait for the authorization redirect (default: 5 minutes).
    /// </summary>
    public TimeSpan AuthorizationTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets whether invalid server certificates (e.g., self-signed certs of a LAN identity provider) are accepted.
    /// DEV-ONLY: should be false in production; install the CA certificate instead.
    /// Only affects the internally created <see cref="HttpClient"/>, not a user-provided one.
    /// </summary>
    public bool DevelopAcceptSelfSignedCerts { get; set; } = false;
}

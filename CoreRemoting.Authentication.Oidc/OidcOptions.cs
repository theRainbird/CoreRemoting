using System;

namespace CoreRemoting.Authentication.Oidc;

/// <summary>
/// Describes the configuration settings of an OIDC authentication provider.
/// </summary>
public class OidcOptions
{
    /// <summary>
    /// Gets or sets the issuer URL of the identity provider (Required).
    /// The JWKS is fetched from the openid-configuration discovery document of this URL.
    /// </summary>
    public string Issuer { get; set; }

    /// <summary>
    /// Gets or sets the audiences that are accepted within the "aud" claim of a token (Required).
    /// A token is valid if its "aud" claim contains at least one of the allowed audiences.
    /// </summary>
    public string[] AllowedAudiences { get; set; }

    /// <summary>
    /// Gets or sets the maximum clock skew that should be tolerated when validating "exp" and "nbf" claims.
    /// </summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets whether an issuer over http (insecure) is allowed. If set to false, the issuer must use https.
    /// </summary>
    public bool AllowInsecureIssuer { get; set; } = false;

    /// <summary>
    /// Gets or sets the name of the claim that contains the roles of the authenticated identity (default: "roles").
    /// Multiple values within that claim are used.
    /// </summary>
    public string RoleClaimName { get; set; } = "roles";

    /// <summary>
    /// Gets or sets an optional delegate for validating step-up codes (Pattern B, multi-phase authentication).
    /// If set, a successful token validation requests the client to provide a step-up code within the same session.
    /// The delegate is invoked with the identity name and the provided code and should return true, if the code is valid.
    /// </summary>
    public Func<string, string, bool> StepUpValidator { get; set; }

    /// <summary>
    /// Gets or sets whether a new random session key should be negotiated within the final authentication response
    /// (both sides switch to that key for message encryption of all following messages). If set to false (default),
    /// the randomly generated handshake key is kept.
    /// </summary>
    public bool NegotiateNewSessionKey { get; set; } = false;

    /// <summary>
    /// Gets or sets whether invalid server certificates (e.g., self-signed certs of a LAN identity provider) are
    /// accepted when fetching the discovery document and the JWKS. DEV-ONLY: should be false in production; install
    /// the CA certificate instead.
    /// </summary>
    public bool DevelopAcceptSelfSignedCerts { get; set; } = false;
}

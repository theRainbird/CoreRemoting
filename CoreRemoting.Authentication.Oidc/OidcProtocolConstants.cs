namespace CoreRemoting.Authentication.Oidc;

/// <summary>
/// OIDC protocol constants shared by OidcAuthenticator (client) and OidcAuthenticationProvider (server).
/// </summary>
public class OidcProtocolConstants
{
    /// <summary>
    /// Name of the credential carrying a JWT issued by the external OpenID Connect identity provider.
    /// </summary>
    public const string OIDC_TOKEN = "oidc_token";

    /// <summary>
    /// Name of the response parameter hinting that an additional step-up factor is required.
    /// </summary>
    public const string STEP_UP_TYPE = "step_up_type";

    /// <summary>
    /// Value of the <see cref="STEP_UP_TYPE"/> parameter for a standard step-up request.
    /// </summary>
    public const string STEP_UP = "oidc_step_up";

    /// <summary>
    /// Name of the credential carrying the step-up factor (e.g., a one-time password).
    /// </summary>
    public const string STEP_UP_CODE = "step_up_code";

    /// <summary>
    /// Optional session identity. Same wire value as SRP's OPTIONAL_SESSION_ID ("X"), so that providers
    /// key their per-session state consistently. Usually not set: the ambient session is used instead.
    /// </summary>
    public const string OPTIONAL_SESSION_ID = "X";
}

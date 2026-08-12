namespace CoreRemoting.Authentication.SecureRemotePassword;

/// <summary>
/// SRP-6a protocol constants used by SrpAuthenticator and SrpAuthenticationProvider.
/// </summary>
public class SrpProtocolConstants
{
    /// <summary>
    /// User name (case sensitive).
    /// </summary>
    public const string USERNAME = "I";

    /// <summary>
    /// Password (never sent to server).
    /// </summary>
    public const string PASSWORD = "P";

    /// <summary>
    /// Salt.
    /// </summary>
    public const string SALT = "s";

    /// <summary>
    /// Client ephemeral public value.
    /// </summary>
    public const string CLIENT_EPHEMERAL_PUBLIC = "A";

    /// <summary>
    /// Server ephemeral public value.
    /// </summary>
    public const string SERVER_EPHEMERAL_PUBLIC = "B";

    /// <summary>
    /// Client session proof.
    /// </summary>
    public const string CLIENT_SESSION_PROOF = "M1";

    /// <summary>
    /// Server session proof.
    /// </summary>
    public const string SERVER_SESSION_PROOF = "M2";

    /// <summary>
    /// Optional session identity.
    /// </summary>
    public const string OPTIONAL_SESSION_ID = "X";
}

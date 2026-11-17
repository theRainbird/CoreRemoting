namespace CoreRemoting.Authentication.JPake;

/// <summary>
/// Protocol constants for J-PAKE message parameters.
/// </summary>
public class JPakeProtocolConstants
{
    // Participant identifiers
    public const string PARTICIPANT_ID_CLIENT = "client";
    public const string PARTICIPANT_ID_SERVER = "server";

    // Message parameter names
    public const string USERNAME = "I";
    public const string PASSWORD = "P";
    public const string OPTIONAL_SESSION_ID = "X";

    // Round 1 parameters
    public const string ROUND1_GX1 = "gx1";
    public const string ROUND1_GX2 = "gx2";
    public const string ROUND1_PROOF_X1 = "proofX1";
    public const string ROUND1_PROOF_X2 = "proofX2";

    // Round 2 parameters
    public const string ROUND2_A = "A";
    public const string ROUND2_PROOF_A = "proofA";

    // Round 3 parameters
    public const string ROUND3_MAC = "MAC";
}

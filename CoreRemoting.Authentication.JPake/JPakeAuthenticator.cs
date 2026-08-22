using System;
using System.Security;
using System.Threading.Tasks;
using CoreRemoting.Toolbox;
using Org.BouncyCastle.Crypto.Agreement.JPake;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Security;
using static CoreRemoting.Authentication.JPake.JPakeProtocolConstants;

namespace CoreRemoting.Authentication.JPake;

/// <summary>
/// Client-side: authentication using the J-PAKE protocol.
/// </summary>
public class JPakeAuthenticator : IAuthenticator
{
    private readonly JPakePrimeOrderGroup _group;
    private readonly SecureRandom _random;

    /// <summary>
    /// Initializes a new instance of the <see cref="JPakeAuthenticator"/> class.
    /// </summary>
    /// <param name="group">Optional J-PAKE prime order group (should match server parameters).</param>
    public JPakeAuthenticator(JPakePrimeOrderGroup group = null)
    {
        _group = group ?? JPakePrimeOrderGroups.NIST_2048;
        _random = new SecureRandom();
    }

    /// <inheritdoc/>
    public async Task<AuthenticationResponseMessage> Authenticate(Credential[] credentials, IAuthenticationProvider authProxy)
    {
        var userName = credentials.FindByName(USERNAME);
        var password = credentials.FindByName(PASSWORD);
        var sessionId = credentials.FindByName(OPTIONAL_SESSION_ID);

        if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Username and password credentials are required for J-PAKE authentication.");

        var participant = new JPakeParticipant(PARTICIPANT_ID_CLIENT, password.ToCharArray(), _group, new Sha256Digest(), _random);

        // === Round 1 ===
        // Client -> Server: gx1, gx2, proofX1, proofX2
        var clientRound1 = participant.CreateRound1PayloadToSend();

        var request1 = new AuthenticationRequestMessage
        {
            Credentials =
            [
                new() { Name = USERNAME, Value = userName },
                new() { Name = ROUND1_GX1, Value = JPakeSerializer.Serialize(clientRound1.Gx1) },
                new() { Name = ROUND1_GX2, Value = JPakeSerializer.Serialize(clientRound1.Gx2) },
                new() { Name = ROUND1_PROOF_X1, Value = JPakeSerializer.Serialize(clientRound1.KnowledgeProofForX1) },
                new() { Name = ROUND1_PROOF_X2, Value = JPakeSerializer.Serialize(clientRound1.KnowledgeProofForX2) },
                new() { Name = OPTIONAL_SESSION_ID, Value = sessionId },
            ],
        };

        // Server -> Client: gx3, gx4, proofX3, proofX4
        var response1 = await authProxy.Authenticate(request1).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(response1.ErrorMessage))
            throw new SecurityException(response1.ErrorMessage);

        var serverRound1 = new JPakeRound1Payload(
            PARTICIPANT_ID_SERVER,
            JPakeSerializer.Deserialize(response1[ROUND1_GX1])[0],
            JPakeSerializer.Deserialize(response1[ROUND1_GX2])[0],
            JPakeSerializer.Deserialize(response1[ROUND1_PROOF_X1]),
            JPakeSerializer.Deserialize(response1[ROUND1_PROOF_X2])
        );

        participant.ValidateRound1PayloadReceived(serverRound1);

        // === Round 2 ===
        // Client -> Server: A, proofA
        var clientRound2 = participant.CreateRound2PayloadToSend();

        var request2 = new AuthenticationRequestMessage
        {
            Credentials =
            [
                new() { Name = ROUND2_A, Value = JPakeSerializer.Serialize(clientRound2.A) },
                new() { Name = ROUND2_PROOF_A, Value = JPakeSerializer.Serialize(clientRound2.KnowledgeProofForX2s) },
                new() { Name = OPTIONAL_SESSION_ID, Value = sessionId },
            ],
        };

        // Server -> Client: B, proofB
        var response2 = await authProxy.Authenticate(request2).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(response2.ErrorMessage))
            throw new SecurityException(response2.ErrorMessage);

        var serverRound2 = new JPakeRound2Payload(
            PARTICIPANT_ID_SERVER,
            JPakeSerializer.Deserialize(response2[ROUND2_A])[0],
            JPakeSerializer.Deserialize(response2[ROUND2_PROOF_A])
        );

        participant.ValidateRound2PayloadReceived(serverRound2);

        // === Round 3 ===
        // Client -> Server: MAC
        var keyingMaterial = participant.CalculateKeyingMaterial();
        var clientRound3 = participant.CreateRound3PayloadToSend(keyingMaterial);

        var request3 = new AuthenticationRequestMessage
        {
            Credentials =
            [
                new() { Name = ROUND3_MAC, Value = JPakeSerializer.Serialize(clientRound3.MacTag) },
                new() { Name = OPTIONAL_SESSION_ID, Value = sessionId },
            ],
        };

        // Server -> Client: MAC + final response
        var response3 = await authProxy.Authenticate(request3).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(response3.ErrorMessage))
            throw new SecurityException(response3.ErrorMessage);

        var serverMacTag = JPakeSerializer.Deserialize(response3[ROUND3_MAC])[0];
        var serverRound3 = new JPakeRound3Payload(
            PARTICIPANT_ID_SERVER,
            serverMacTag);
        participant.ValidateRound3PayloadReceived(serverRound3, keyingMaterial);

        // Derive negotiated shared key
        response3.NegotiatedSharedKey = keyingMaterial.ToByteArray();
        return response3;
    }
}

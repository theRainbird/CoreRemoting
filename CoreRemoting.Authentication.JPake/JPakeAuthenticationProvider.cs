using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto.Agreement.JPake;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Security;
using static CoreRemoting.Authentication.JPake.JPakeProtocolConstants;

namespace CoreRemoting.Authentication.JPake;

/// <summary>
/// Server-side: authentication provider for the J-PAKE protocol.
/// </summary>
public class JPakeAuthenticationProvider : IAuthenticationProvider
{
    private readonly IJPakeAccountRepository _repository;
    private readonly JPakePrimeOrderGroup _group;
    private readonly SecureRandom _random;
    private readonly string _unknownUserPassword;

    internal ConcurrentDictionary<string, SessionData> PendingAuthentications { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="JPakeAuthenticationProvider"/> class.
    /// </summary>
    /// <param name="repository">User account repository.</param>
    /// <param name="group">Optional J-PAKE prime order group (should match client parameters).</param>
    public JPakeAuthenticationProvider(IJPakeAccountRepository repository, JPakePrimeOrderGroup group = null)
    {
        _repository = repository;
        _group = group ?? JPakePrimeOrderGroups.NIST_2048;
        _random = new SecureRandom();
        _unknownUserPassword = GenerateRandomPassword();
    }

    /// <summary>
    /// Session data stored between J-PAKE rounds.
    /// </summary>
    internal class SessionData
    {
        public IJPakeAccount Account { get; set; }
        public JPakeParticipant Participant { get; set; }
    }

    /// <inheritdoc/>
    public async Task<AuthenticationResponseMessage> Authenticate(AuthenticationRequestMessage authRequest)
    {
        // Determine round by message content
        if (authRequest[USERNAME] != null)
            return await Round1(authRequest).ConfigureAwait(false);

        if (authRequest[ROUND2_A] != null)
            return Round2(authRequest);

        if (authRequest[ROUND3_MAC] != null)
            return await Round3(authRequest).ConfigureAwait(false);

        // Invalid J-PAKE request
        return Error();
    }

    private async Task<AuthenticationResponseMessage> Round1(AuthenticationRequestMessage authRequest)
    {
        var userName = authRequest[USERNAME];
        var sessionId = authRequest[OPTIONAL_SESSION_ID] ?? RemotingSession.Current.SessionId.ToString();

        // Find account or use fake password for non-existent users
        var account = await _repository.FindByName(userName).ConfigureAwait(false);
        var password = account != null
            ? account.Password
            : _unknownUserPassword;

        // Create server's Round 1
        var serverParticipant = new JPakeParticipant(PARTICIPANT_ID_SERVER, password.ToCharArray(), _group, new Sha256Digest(), _random);
        var serverRound1 = serverParticipant.CreateRound1PayloadToSend();

        // Process client's Round 1
        var clientRound1 = new JPakeRound1Payload(
            PARTICIPANT_ID_CLIENT,
            JPakeSerializer.Deserialize(authRequest[ROUND1_GX1])[0],
            JPakeSerializer.Deserialize(authRequest[ROUND1_GX2])[0],
            JPakeSerializer.Deserialize(authRequest[ROUND1_PROOF_X1]),
            JPakeSerializer.Deserialize(authRequest[ROUND1_PROOF_X2])
        );

        serverParticipant.ValidateRound1PayloadReceived(clientRound1);

        // Save session data for subsequent rounds
        PendingAuthentications[sessionId] = new SessionData
        {
            Account = account,
            Participant = serverParticipant,
        };

        return new AuthenticationResponseMessage
        {
            IsCompleted = false,
            IsAuthenticated = false,
            Parameters =
            [
                new() { Name = ROUND1_GX1, Value = JPakeSerializer.Serialize(serverRound1.Gx1) },
                new() { Name = ROUND1_GX2, Value = JPakeSerializer.Serialize(serverRound1.Gx2) },
                new() { Name = ROUND1_PROOF_X1, Value = JPakeSerializer.Serialize(serverRound1.KnowledgeProofForX1) },
                new() { Name = ROUND1_PROOF_X2, Value = JPakeSerializer.Serialize(serverRound1.KnowledgeProofForX2) },
            ],
        };
    }

    private AuthenticationResponseMessage Round2(AuthenticationRequestMessage authRequest)
    {
        var sessionId = authRequest[OPTIONAL_SESSION_ID] ?? RemotingSession.Current.SessionId.ToString();

        // Session not found or expired
        if (!PendingAuthentications.TryGetValue(sessionId, out var session))
            return Error();

        // Create server's Round 2
        var serverRound2 = session.Participant.CreateRound2PayloadToSend();

        // Process client's Round 2
        var clientRound2 = new JPakeRound2Payload(
            PARTICIPANT_ID_CLIENT,
            JPakeSerializer.Deserialize(authRequest[ROUND2_A])[0],
            JPakeSerializer.Deserialize(authRequest[ROUND2_PROOF_A])
        );

        try
        {
            session.Participant.ValidateRound2PayloadReceived(clientRound2);
        }
        catch (Exception)
        {
            // J-PAKE Round 2 validation failed
            PendingAuthentications.TryRemove(sessionId, out _);
            return Error();
        }

        return new AuthenticationResponseMessage
        {
            IsCompleted = false,
            IsAuthenticated = false,
            Parameters =
            [
                new() { Name = ROUND2_A, Value = JPakeSerializer.Serialize(serverRound2.A) },
                new() { Name = ROUND2_PROOF_A, Value = JPakeSerializer.Serialize(serverRound2.KnowledgeProofForX2s) },
            ],
        };
    }

    private async Task<AuthenticationResponseMessage> Round3(AuthenticationRequestMessage authRequest)
    {
        var sessionId = authRequest[OPTIONAL_SESSION_ID] ?? RemotingSession.Current.SessionId.ToString();

        // Session not found or expired
        if (!PendingAuthentications.TryRemove(sessionId, out var session))
            return Error();

        // Calculate shared secret key
        var keyingMaterial = session.Participant.CalculateKeyingMaterial();

        // Create server's Round 3 (confirmation)
        var serverRound3 = session.Participant.CreateRound3PayloadToSend(keyingMaterial);

        // Process client's Round 3
        var clientMacTag = JPakeSerializer.Deserialize(authRequest[ROUND3_MAC])[0];
        var clientRound3 = new JPakeRound3Payload(
            PARTICIPANT_ID_CLIENT,
            clientMacTag);

        try
        {
            session.Participant.ValidateRound3PayloadReceived(clientRound3, keyingMaterial);
        }
        catch (Exception)
        {
            return Error();
        }

        // Verify that account exists
        if (session.Account == null)
            return Error();

        var identity = await _repository.GetIdentity(session.Account).ConfigureAwait(false);

        var response = new AuthenticationResponseMessage
        {
            IsCompleted = true,
            IsAuthenticated = true,
            AuthenticatedIdentity = identity,
            NegotiatedSharedKey = keyingMaterial.ToByteArray(),
            Parameters =
            [
                new() { Name = ROUND3_MAC, Value = JPakeSerializer.Serialize(serverRound3.MacTag) },
            ],
        };

        return response;
    }

    /// <summary>
    /// Generates a random password for unknown users to prevent user enumeration.
    /// </summary>
    private static string GenerateRandomPassword(int length = 32)
    {
        using var rng = RandomNumberGenerator.Create();
        var bytesNeeded = (length * 3 + 3) / 4; // ceil(length * 3 / 4)
        var bytes = new byte[bytesNeeded];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Substring(0, length);
    }

    private static AuthenticationResponseMessage Error(string message = "Authentication failed: bad password or user name") => new()
    {
        IsCompleted = true,
        IsAuthenticated = false,
        ErrorMessage = message,
    };
}

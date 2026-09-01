using System.Linq;
using System.Security;
using System.Threading.Tasks;
using CoreRemoting.Toolbox;
using SecureRemotePassword;
using static CoreRemoting.Authentication.SecureRemotePassword.SrpProtocolConstants;

namespace CoreRemoting.Authentication.SecureRemotePassword;

/// <summary>
/// Client-side: credentials for the SRP-6a authentication protocol.
/// </summary>
public class SrpAuthenticator : IAuthenticator
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SrpAuthenticator"/> class.
    /// </summary>
    /// <param name="parameters">Optional SRP-6a protocol parameters (should match server parameters).</param>
    public SrpAuthenticator(SrpParameters parameters = null)
    {
        SrpClient = new SrpClient(parameters);
    }

    private SrpClient SrpClient { get; set; }

    /// <inheritdoc/>
    public async Task<AuthenticationResponseMessage> Authenticate(Credential[] credentials, IAuthenticationProvider authProxy)
    {
        var userName = credentials.FindByName(USERNAME);
        var password = credentials.FindByName(PASSWORD);

        // step1 request: User -> Host: I, A = g^a (identifies self, a = random number)
        var clientEphemeral = SrpClient.GenerateEphemeral();
        var request1 = new AuthenticationRequestMessage
        {
            Credentials =
            [
                new() { Name = USERNAME, Value = userName },
                new() { Name = CLIENT_EPHEMERAL_PUBLIC, Value = clientEphemeral.Public },
            ],
        };

        // step1 response: Host -> User: s, B = kv + g^b (sends salt, b = random number)
        var response1 = await authProxy.Authenticate(request1).ConfigureAwait(false);
        var salt = response1[SALT];
        var serverEphemeralPublic = response1[SERVER_EPHEMERAL_PUBLIC];

        // step2 request: User -> Host: M = H(H(N) xor H(g), H(I), s, A, B, K)
        var privateKey = SrpClient.DerivePrivateKey(salt, userName, password);
        var clientSession = SrpClient.DeriveSession(clientEphemeral.Secret, serverEphemeralPublic, salt, userName, privateKey);
        var request2 = new AuthenticationRequestMessage
        {
            Credentials =
            [
                new() { Name = CLIENT_SESSION_PROOF, Value = clientSession.Proof },
            ],
        };

        // step2 response: Host -> User: H(A, M, K)
        var response2 = await authProxy.Authenticate(request2).ConfigureAwait(false);
        var serverSessionProof = response2[SERVER_SESSION_PROOF];
        SrpClient.VerifySession(clientEphemeral.Public, clientSession, serverSessionProof);

        // if shared key is negotiated, check that keying material wasn't sent over the wire
        if (response2.NegotiatedSharedKey is {} key)
        {
            if (key.ContainsKeyMaterial)
                throw new SecurityException("Negotiated shared key is compromised.");

            // restore the negotiated SRP session key using the locally derived key
            response2.NegotiatedSharedKey =
                new(SrpInteger.FromHex(clientSession.Key).ToByteArray());
        }

        return response2;
    }
}

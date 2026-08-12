using System;
using System.Collections.Concurrent;
using System.Security;
using System.Threading.Tasks;
using SecureRemotePassword;
using static CoreRemoting.Authentication.SecureRemotePassword.SrpProtocolConstants;
using static CoreRemoting.Authentication.AuthenticationResponseMessage;

namespace CoreRemoting.Authentication.SecureRemotePassword;

/// <summary>
/// Server-side: authentication provider for the SRP-6a protocol.
/// </summary>
/// <seealso cref="IAuthenticationProvider" />
public class SrpAuthenticationProvider : IAuthenticationProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SrpAuthenticationProvider"/> class.
    /// </summary>
    /// <param name="repository">User account repository.</param>
    /// <param name="parameters">Optional SRP-6a protocol parameters.</param>
    public SrpAuthenticationProvider(ISrpAccountRepository repository, SrpParameters parameters = null)
    {
        AuthRepository = repository;
        SrpParameters = parameters ?? new();
        SrpServer = new SrpServer(SrpParameters);
        UnknownUserSalt = new SrpClient(parameters).GenerateSalt();
    }

    private ISrpAccountRepository AuthRepository { get; set; }

    private SrpParameters SrpParameters { get; set; }

    private SrpServer SrpServer { get; set; }

    private string UnknownUserSalt { get; set; }

    internal ConcurrentDictionary<string, Step1Data> PendingAuthentications { get; } =
        new ConcurrentDictionary<string, Step1Data>();

    // variables produced on the first authentication step
    internal class Step1Data
    {
        public ISrpAccount Account { get; set; }
        public string ClientEphemeralPublic { get; set; }
        public SrpEphemeral ServerEphemeral { get; set; }
    }

    // Fake account is used so the attacker cannot tell whether the given user exists or not
    internal class FakeSrpAccount : ISrpAccount
    {
        public string UserName { get; set; }
        public string Salt => "1234";
        public string Verifier => "4321";
    }

    /// <inheritdoc/>
    public Task<AuthenticationResponseMessage> Authenticate(AuthenticationRequestMessage authRequest)
    {
        // step1: username + client ephemeral
        var userName = authRequest[USERNAME];

        if (!string.IsNullOrWhiteSpace(userName))
            return AuthStep1(authRequest);

        // step2: client session proof
        return AuthStep2(authRequest);
    }

    private async Task<AuthenticationResponseMessage> AuthStep1(AuthenticationRequestMessage authRequest)
    {
        // first step never fails: User -> Host: I, A = g^a (identifies self, a = random number)
        var userName = authRequest[USERNAME];
        var clientEphemeral = authRequest[CLIENT_EPHEMERAL_PUBLIC];
        var sessionId = authRequest[OPTIONAL_SESSION_ID] ?? RemotingSession.Current.SessionId.ToString();

        var account = await AuthRepository.FindByName(userName);
        if (account != null)
        {
            // save the data for the second authentication step
            var salt = account.Salt;
            var verifier = account.Verifier;
            var serverEphemeral = SrpServer.GenerateEphemeral(verifier);

            PendingAuthentications[sessionId] = new Step1Data
            {
                Account = account,
                ClientEphemeralPublic = clientEphemeral,
                ServerEphemeral = serverEphemeral,
            };

            // Host -> User: s, B = kv + g^b (sends salt, b = random number)
            return ResponseStep1(salt, serverEphemeral.Public);
        }

        // generate fake salt and B values so that attacker cannot tell whether the given user exists or not
        var fakeSalt = SrpParameters.Hash(userName + UnknownUserSalt).ToHex();
        var fakeEphemeral = SrpServer.GenerateEphemeral(fakeSalt);

        PendingAuthentications[sessionId] = new Step1Data
        {
            Account = new FakeSrpAccount { UserName = userName },
            ClientEphemeralPublic = clientEphemeral,
            ServerEphemeral = fakeEphemeral
        };

        return ResponseStep1(fakeSalt, fakeEphemeral.Public);
    }

    private async Task<AuthenticationResponseMessage> AuthStep2(AuthenticationRequestMessage authRequest)
    {
        try
        {
            // get the values calculated on the first step
            var sessionId = authRequest[OPTIONAL_SESSION_ID] ?? RemotingSession.Current.SessionId.ToString();
            if (!PendingAuthentications.TryRemove(sessionId, out var vars) || vars.Account is FakeSrpAccount)
                throw new SecurityException();

            // second step may fail: User -> Host: M = H(H(N) xor H(g), H(I), s, A, B, K)
            var clientSessionProof = authRequest[CLIENT_SESSION_PROOF];
            var serverSession = SrpServer.DeriveSession(vars.ServerEphemeral.Secret, vars.ClientEphemeralPublic,
                vars.Account.Salt, vars.Account.UserName, vars.Account.Verifier, clientSessionProof);

            // Host -> User: H(A, M, K)
            return await ResponseStep2(serverSession.Proof, vars.Account);
        }
        catch (SecurityException)
        {
            return Error("Authentication failed: bad password or user name");
        }
    }

    private AuthenticationResponseMessage ResponseStep1(string salt, string serverEphemeralPublic) => new()
    {
        IsCompleted = false,
        IsAuthenticated = false,
        Parameters =
        [
            new() { Name = SALT, Value = salt },
            new() { Name = SERVER_EPHEMERAL_PUBLIC, Value = serverEphemeralPublic},
        ],
    };

    private async Task<AuthenticationResponseMessage> ResponseStep2(string serverSessionProof, ISrpAccount account) => new()
    {
        IsCompleted = true,
        IsAuthenticated = true,
        AuthenticatedIdentity = await AuthRepository.GetIdentity(account),
        Parameters =
        [
            new() { Name = SERVER_SESSION_PROOF, Value = serverSessionProof },
        ],
    };
}

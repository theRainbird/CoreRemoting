using System;
using System.Security;
using System.Threading.Tasks;

namespace CoreRemoting.Authentication.Oidc;

/// <summary>
/// Client-side: acquires an OIDC token (and an optional step-up code when the server requests it) and submits
/// them to the server as authentication credentials.
/// </summary>
public class OidcAuthenticator : IAuthenticator
{
    private readonly Func<Task<string>> _tokenAcquirer;
    private readonly Func<string, Task<string>> _stepUpPrompt;

    /// <summary>
    /// Initializes a new instance of the <see cref="OidcAuthenticator"/> class.
    /// </summary>
    /// <param name="tokenAcquirer">Delegate that returns the OIDC token to be submitted (e.g., from an identity broker).</param>
    public OidcAuthenticator(Func<Task<string>> tokenAcquirer) : this(tokenAcquirer, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OidcAuthenticator"/> class.
    /// </summary>
    /// <param name="tokenAcquirer">Delegate that returns the OIDC token to be submitted (e.g., from an identity broker).</param>
    /// <param name="stepUpPrompt">Optional delegate that requests a step-up code from the user (the server must request one within the same session).</param>
    public OidcAuthenticator(Func<Task<string>> tokenAcquirer, Func<string, Task<string>> stepUpPrompt)
    {
        _tokenAcquirer = tokenAcquirer ?? throw new ArgumentNullException(nameof(tokenAcquirer));
        _stepUpPrompt = stepUpPrompt;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OidcAuthenticator"/> class using an OIDC token requirer.
    /// The requirer performs the Authorization Code flow with PKCE against an identity provider and returns the token.
    /// </summary>
    /// <param name="tokenAcquirer">OIDC token requirer that acquires the token to be submitted.</param>
    public OidcAuthenticator(OidcTokenAcquirer tokenAcquirer)
    {
        if (tokenAcquirer == null)
            throw new ArgumentNullException(nameof(tokenAcquirer));

        _tokenAcquirer = () => tokenAcquirer.GetTokenAsync();
        _stepUpPrompt = null;
    }

    /// <summary>
    /// Authenticates the client with the provided token (and a step-up code, if requested by the server).
    /// </summary>
    /// <exception cref="SecurityException">Thrown, if the authentication failed or no step-up prompt was provided</exception>
    public async Task<AuthenticationResponseMessage> Authenticate(Credential[] credentials, IAuthenticationProvider authProxy)
    {
        if (authProxy == null)
            throw new ArgumentNullException(nameof(authProxy));

        var token = await _tokenAcquirer().ConfigureAwait(false);

        var requestCredentials = AppendCredential(credentials, OidcProtocolConstants.OIDC_TOKEN, token);

        var response = await authProxy
            .Authenticate(new AuthenticationRequestMessage { Credentials = requestCredentials })
            .ConfigureAwait(false);

        // multi-phase: while the server doesn't consider the authentication completed,
        // it can request a step-up code (pattern B)
        while (!response.IsCompleted)
        {
            if (_stepUpPrompt == null)
                throw new SecurityException(
                    "The server requested a step-up verification, but no step-up prompt delegate was provided.");

            var stepUpType = response[OidcProtocolConstants.STEP_UP_TYPE];
            var stepUpCode = await _stepUpPrompt(stepUpType).ConfigureAwait(false);

            requestCredentials = AppendCredential(requestCredentials, OidcProtocolConstants.STEP_UP_CODE, stepUpCode);

            response = await authProxy
                .Authenticate(new AuthenticationRequestMessage { Credentials = requestCredentials })
                .ConfigureAwait(false);
        }

        if (!response.IsAuthenticated)
            throw new SecurityException(response.ErrorMessage ?? "OIDC authentication failed.");

        return response;
    }

    private static Credential[] AppendCredential(Credential[] credentials, string name, string value) =>
        [
            ..(credentials ?? Array.Empty<Credential>()),
            new() { Name = name, Value = value },
        ];
}

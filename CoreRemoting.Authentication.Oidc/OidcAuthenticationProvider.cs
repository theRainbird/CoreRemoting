using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Security;
using CoreRemoting;

namespace CoreRemoting.Authentication.Oidc;

/// <summary>
/// Validates OpenID Connect JWTs provided by clients against the JWKS of an identity provider (Pattern A).
/// Optionally, a step-up code can be requested from the client within the same session when a validator delegate is
/// configured (Pattern B, multi-phase authentication based on IsCompleted/Parameters). An optional fallback provider
/// is used when no OIDC token was provided ("OIDC first" chaining).
/// </summary>
public class OidcAuthenticationProvider : IOidcAuthenticationProvider
{
    private readonly OidcOptions _options;
    private readonly IAuthenticationProvider _fallbackProvider;
    private readonly JwksCache _jwksCache;
    private readonly ConcurrentDictionary<string, RemotingIdentity> _pendingAuthentications = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="OidcAuthenticationProvider"/> class.
    /// </summary>
    /// <param name="options">OIDC options</param>
    public OidcAuthenticationProvider(OidcOptions options) : this(options, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OidcAuthenticationProvider"/> class.
    /// </summary>
    /// <param name="options">OIDC options</param>
    /// <param name="fallbackProvider">Optional authentication provider that is used, if no OIDC token was provided.</param>
    public OidcAuthenticationProvider(OidcOptions options, IAuthenticationProvider fallbackProvider)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(options.Issuer))
            throw new ArgumentException("OidcOptions.Issuer must not be empty.", nameof(options));

        if (options.AllowedAudiences == null ||
            options.AllowedAudiences.Length == 0 ||
            options.AllowedAudiences.Any(audience => string.IsNullOrEmpty(audience)))
            throw new ArgumentException(
                "OidcOptions.AllowedAudiences must contain at least one non-empty audience.", nameof(options));

        var issuerUri = new Uri(options.Issuer, UriKind.Absolute);
        if (issuerUri.Scheme == Uri.UriSchemeHttp && !options.AllowInsecureIssuer && !IsLocalhost(issuerUri.Host))
            throw new SecurityException(
                "OidcOptions.Issuer must use https unless AllowInsecureIssuer is explicitly set to true.");

        _options = options;
        _fallbackProvider = fallbackProvider;
        _jwksCache = new JwksCache(options.Issuer);
        JwksCache.AcceptSelfSignedCerts = options.DevelopAcceptSelfSignedCerts;
    }

    /// <summary>
    /// Authenticates a client with the provided OIDC token or step-up code.
    /// </summary>
    /// <param name="authRequestMessage">Authentication request (contains the 'oidc_token'/'step_up_code' credential)</param>
    /// <returns>Authentication response</returns>
    async Task<AuthenticationResponseMessage> IAuthenticationProvider.Authenticate(AuthenticationRequestMessage authRequestMessage)
    {
        if (authRequestMessage == null)
            throw new ArgumentNullException(nameof(authRequestMessage));

        var stateKey = GetStateKey(authRequestMessage);

        // step-up request ('step_up_code' is only allowed after a successful token validation within this session)
        if (TryGetCredential(authRequestMessage, OidcProtocolConstants.STEP_UP_CODE, out var stepUpCode))
            return ProcessStepUp(stateKey, stepUpCode);

        // token request: the client provides an OIDC token that is validated against the JWKS
        if (!TryGetCredential(authRequestMessage, OidcProtocolConstants.OIDC_TOKEN, out var token))
            return _fallbackProvider != null
                ? await _fallbackProvider.Authenticate(authRequestMessage).ConfigureAwait(false)
                : Fail("No OIDC token was provided.");

        RemotingIdentity identity;
        try
        {
            var validatedToken =
                await OpenIdTokenValidator.ValidateAsync(token, _options, _jwksCache).ConfigureAwait(false);

            identity = BuildIdentity(validatedToken);
        }
        catch (Exception e)
        {
            return Fail($"The OIDC token validation failed: {e.Message}");
        }

        // without a step-up validator, the authentication is complete after validating the token
        if (_options.StepUpValidator == null)
            return Success(identity);

        // pattern B: remember the validated identity and request a step-up code from the client
        _pendingAuthentications[stateKey] = identity;

        return new AuthenticationResponseMessage
        {
            IsAuthenticated = false,
            // not completed yet — the client should provide a 'step_up_code' credential within this session
            IsCompleted = false,
            Parameters =
            [
                new() { Name = OidcProtocolConstants.STEP_UP_TYPE, Value = OidcProtocolConstants.STEP_UP },
            ],
        };
    }

    /// <summary>
    /// Processes a step-up code (pattern B).
    /// </summary>
    private AuthenticationResponseMessage ProcessStepUp(string stateKey, string stepUpCode)
    {
        if (!_pendingAuthentications.TryRemove(stateKey, out var identity))
            return Fail("There's no pending OIDC step-up validation for this session.");

        // the validator delegate is invoked with the validated identity name and the provided code
        if (_options.StepUpValidator(identity.Name, stepUpCode))
            return Success(identity);

        return Fail("The provided step-up code was invalid.");
    }

    private RemotingIdentity BuildIdentity(ValidatedOidcToken validatedToken)
    {
        var identity = new RemotingIdentity
        {
            Name = validatedToken.Subject,
            AuthenticationType = "OIDC",
            IsAuthenticated = true,
            Claims = validatedToken.Claims,
            Roles = Array.Empty<string>(),
        };

        if (validatedToken.Claims.TryGetValue(
                _options.RoleClaimName ?? "roles", out var roles) &&
            roles.Length > 0)
            identity.Roles = roles;

        return identity;
    }

    private AuthenticationResponseMessage Fail(string errorMessage) =>
        new()
        {
            IsAuthenticated = false,
            // completed — the client is informed about the failure reason
            ErrorMessage = errorMessage,
        };

    private AuthenticationResponseMessage Success(RemotingIdentity identity)
    {
        var response = new AuthenticationResponseMessage
        {
            IsAuthenticated = true,
            // completed — both sides can proceed with regular (possibly re-keyed) message exchange
            AuthenticatedIdentity = identity,
        };

        if (_options.NegotiateNewSessionKey)
        {
            using var randomGenerator = new RNGCryptoServiceProvider();
            var sessionKey = new byte[32];
            randomGenerator.GetBytes(sessionKey);
            response.NegotiatedSharedKey = sessionKey;
        }

        return response;
    }

    private static bool TryGetCredential(AuthenticationRequestMessage authRequestMessage, string name, out string value)
    {
        if (authRequestMessage[name] is { Length: > 0 } credentialValue)
        {
            value = credentialValue;
            return true;
        }

        value = null;
        return false;
    }

    private static string GetStateKey(AuthenticationRequestMessage authRequestMessage) =>
        authRequestMessage[OidcProtocolConstants.OPTIONAL_SESSION_ID]
        ?? RemotingSession.Current?.SessionId.ToString()
        ?? "no-session";

    private static bool IsLocalhost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "127.0.0.1", StringComparison.Ordinal);
}

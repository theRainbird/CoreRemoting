using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CoreRemoting.Authentication.Oidc;

/// <summary>
/// Represents a validated OIDC token.
/// </summary>
internal class ValidatedOidcToken
{
    internal ValidatedOidcToken(string subject, IDictionary<string, string[]> claims)
    {
        Subject = subject;
        Claims = claims;
    }

    /// <summary>
    /// Gets the subject ("sub" claim).
    /// </summary>
    public string Subject { get; }

    /// <summary>
    /// Gets all claims of the token (each mapped to its string values).
    /// </summary>
    public IDictionary<string, string[]> Claims { get; }
}

/// <summary>
/// Validates OpenID Connect JWTs (signed with RS256) against the JWKS of an identity provider.
/// </summary>
internal static class OpenIdTokenValidator
{
    /// <summary>
    /// Validates a JWT.
    /// </summary>
    /// <param name="token">JWT to be validated</param>
    /// <param name="options">OIDC options (issuer, allowed audiences and clock skew)</param>
    /// <param name="jwksCache">JWKS cache of the identity provider</param>
    /// <returns>Validated token with subject and all claims</returns>
    /// <exception cref="SecurityException">Thrown, if the token is invalid</exception>
    public static async Task<ValidatedOidcToken> ValidateAsync(string token, OidcOptions options, JwksCache jwksCache)
    {
        if (token == null)
            throw new ArgumentNullException(nameof(token));

        var segments = token.Split('.');
        if (segments.Length != 3)
            throw new SecurityException("The provided OIDC token is malformed.");

        // header
        JObject header;
        try
        {
            header = JObject.Parse(Encoding.UTF8.GetString(Base64Url.Decode(segments[0])));
        }
        catch (Exception e) when (e is not SecurityException)
        {
            throw new SecurityException($"The header of the OIDC token couldn't be parsed: {e.Message}", e);
        }

        if ((string)header["alg"] != "RS256")
            throw new SecurityException("The OIDC token was signed with an unsupported algorithm (only RS256 is supported).");

        // payload (contains all claims)
        IDictionary<string, string[]> claims;
        try
        {
            claims = ParseClaims(segments[1]);
        }
        catch (Exception e) when (e is not SecurityException)
        {
            throw new SecurityException($"The claims of the OIDC token couldn't be parsed: {e.Message}", e);
        }

        // validate standard claims
        if (TryGetValue(claims, "sub", out var subject) == false || string.IsNullOrEmpty(subject))
            throw new SecurityException("The OIDC token doesn't contain a 'sub' claim.");

        var expectedIssuer = options.Issuer.TrimEnd('/');
        if (TryGetValue(claims, "iss", out var issuer) == false ||
            !string.Equals(issuer?.TrimEnd('/'), expectedIssuer, StringComparison.Ordinal))
            throw new SecurityException($"The OIDC token was issued by '{issuer ?? ""}', but '{expectedIssuer}' was expected.");

        if (!claims.TryGetValue("aud", out var audiences) ||
            !audiences.Any(audience => options.AllowedAudiences
                .Any(allowedAudience => string.Equals(allowedAudience, audience, StringComparison.Ordinal))))
            throw new SecurityException("The OIDC token wasn't issued for any of the allowed audiences.");

        // RFC 7519 §2.3: if the token has multiple audiences, the authorized party (azp) claim must be present
        // and equal to one of the audiences.
        if (audiences.Length > 1)
        {
            if (!TryGetValue(claims, "azp", out var authorizedParty) || string.IsNullOrEmpty(authorizedParty))
                throw new SecurityException(
                    "The OIDC token with multiple audiences is missing the 'azp' (authorized party) claim.");

            if (!audiences.Any(audience => string.Equals(audience, authorizedParty, StringComparison.Ordinal)))
                throw new SecurityException(
                    "The 'azp' (authorized party) claim of the OIDC token doesn't match any of its audiences.");
        }

        if (TryGetValue(claims, "exp", out var expiryValue) == false ||
            !long.TryParse(expiryValue, out var expirySeconds))
            throw new SecurityException("The OIDC token doesn't contain a valid 'exp' claim.");

        var expiry = DateTimeOffset.FromUnixTimeSeconds(expirySeconds);
        if (expiry.Add(options.ClockSkew) < DateTimeOffset.UtcNow)
            throw new SecurityException("The OIDC token has expired.");

        if (TryGetValue(claims, "nbf", out var notBeforeValue) &&
            long.TryParse(notBeforeValue, out var notBeforeSeconds))
        {
            var notBefore = DateTimeOffset.FromUnixTimeSeconds(notBeforeSeconds);
            if (notBefore > DateTimeOffset.UtcNow + options.ClockSkew)
                throw new SecurityException("The OIDC token isn't valid yet.");
        }

        // verify signature with the JWKS public key
        var kid = (string)header["kid"];
        var keys = await jwksCache.GetKeysAsync().ConfigureAwait(false);

        var key = ResolveKey(keys, kid);

        // refresh the JWKS once, if a known kid couldn't be resolved (key rotation)
        if (key == null && kid != null)
        {
            keys = await jwksCache.GetKeysAsync(forceRefresh: true).ConfigureAwait(false);
            key = ResolveKey(keys, kid);
        }

        if (key == null)
            throw new SecurityException(
                $"The JWKS doesn't contain a public key{(kid == null ? "" : $" with 'kid' '{kid}'")} for this OIDC token.");

        byte[] signature;
        try
        {
            signature = Base64Url.Decode(segments[2]);
        }
        catch (Exception e) when (e is not SecurityException)
        {
            throw new SecurityException($"The signature of the OIDC token couldn't be parsed: {e.Message}", e);
        }

        using var rsa = RSA.Create();
        rsa.ImportParameters(key.RsaParameters);

        var signedData = Encoding.UTF8.GetBytes(segments[0] + "." + segments[1]);

        if (!rsa.VerifyData(signedData, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new SecurityException("The signature of the OIDC token is invalid.");

        return new ValidatedOidcToken(subject, claims);
    }

    private static bool TryGetValue(IDictionary<string, string[]> claims, string name, out string value)
    {
        if (claims.TryGetValue(name, out var values) && values.Length > 0)
        {
            value = values[0];
            return true;
        }

        value = null;
        return false;
    }

    private static JwksKey ResolveKey(JwksKey[] keys, string kid)
    {
        if (kid != null)
        {
            var matchingKey = keys.FirstOrDefault(
                key => string.Equals(key.Kid, kid, StringComparison.Ordinal));

            return matchingKey;
        }

        // no 'kid' in header: only resolvable, when the JWKS contains exactly one public key
        return keys.Length == 1 ? keys[0] : null;
    }

    private static IDictionary<string, string[]> ParseClaims(string segment)
    {
        var payload = JObject.Parse(Encoding.UTF8.GetString(Base64Url.Decode(segment)));

        var claims = new Dictionary<string, string[]>(payload.Count);

        foreach (var property in payload.Properties())
        {
            claims[property.Name] =
                property.Value.Type == JTokenType.Array
                    ? property.Value.Values<string>().ToArray()
                    : [property.Value.ToString()];
        }

        return claims;
    }
}

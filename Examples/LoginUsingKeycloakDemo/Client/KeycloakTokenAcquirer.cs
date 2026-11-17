using System;
using System.Linq;
using CoreRemoting.Authentication.Oidc;

class KeycloakTokenAcquirer : OidcTokenAcquirer
{
    private KeycloakTokenAcquirer(OidcClientOptions options) : base(options)
    {
    }

    public static KeycloakTokenAcquirer FromEnvironment()
    {
        var issuer = GetRequiredEnvironmentVariable("KEYCLOAK_ISSUER");
        var clientId = GetRequiredEnvironmentVariable("KEYCLOAK_CLIENT_ID");

        var clientSecret = Environment.GetEnvironmentVariable("KEYCLOAK_CLIENT_SECRET");
        var redirectUri = Environment.GetEnvironmentVariable("KEYCLOAK_REDIRECT_URI");
        var scopesRaw = Environment.GetEnvironmentVariable("KEYCLOAK_SCOPES");

        var options = new OidcClientOptions
        {
            Issuer = issuer,
            ClientId = clientId,
            ClientSecret = string.IsNullOrWhiteSpace(clientSecret) ? null : clientSecret,
            RedirectUri = string.IsNullOrWhiteSpace(redirectUri) ? null : redirectUri,
            Scopes = string.IsNullOrWhiteSpace(scopesRaw)
                ? new[] { "openid", "profile" }
                : scopesRaw.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).ToArray(),
            DevelopAcceptSelfSignedCerts = IsTrueEnvironmentVariable("KEYCLOAK_ACCEPT_SELF_SIGNED_CERTS"),
        };

        return new KeycloakTokenAcquirer(options);
    }

    private static string GetRequiredEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"The environment variable '{name}' is required but not set. " +
                "See .env.example for available variables.");

        return value;
    }

    private static bool IsTrueEnvironmentVariable(string name)
        => string.Equals(Environment.GetEnvironmentVariable(name), "true", StringComparison.OrdinalIgnoreCase);
}

using System;
using CoreRemoting;
using CoreRemoting.Authentication.Oidc;
using static Logger<Server>;

class Server
{
    public static IRemotingServer Start()
    {
        var issuer = Environment.GetEnvironmentVariable("KEYCLOAK_ISSUER");
        var clientId = Environment.GetEnvironmentVariable("KEYCLOAK_CLIENT_ID");

        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException(
                "The server requires the environment variables KEYCLOAK_ISSUER and KEYCLOAK_CLIENT_ID.");

        var server = new RemotingServer(new()
        {
            HostName = "localhost",
            NetworkPort = 9292,
            MessageEncryption = true,
            KeySize = 1024,
            AuthenticationRequired = true,
            AuthenticationProvider = new OidcAuthenticationProvider(new OidcOptions
            {
                Issuer = issuer,
                AllowedAudiences = new[] { clientId },
                DevelopAcceptSelfSignedCerts = IsTrueEnvironmentVariable(
                    "KEYCLOAK_ACCEPT_SELF_SIGNED_CERTS"),
            }),
            RegisterServicesAction = container =>
                container.RegisterService<ISampleService, SampleService>()
        });

        server.Start();
        WriteLine("Started. Starting client...");
        return server;
    }

    private static bool IsTrueEnvironmentVariable(string name)
        => string.Equals(Environment.GetEnvironmentVariable(name), "true", StringComparison.OrdinalIgnoreCase);
}

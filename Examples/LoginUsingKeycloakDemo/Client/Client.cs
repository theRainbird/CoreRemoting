using CoreRemoting;
using CoreRemoting.Authentication.Oidc;
using static Logger<Client>;

class Client
{
    public static IRemotingClient Start()
    {
        WriteLine("Connecting to the server using Keycloak (OIDC) authentication.");
        WriteLine("Press Enter to start the browser-based login...");
        ReadLine();

        var client = new RemotingClient(new()
        {
            ServerHostName = "localhost",
            ServerPort = 9292,
            MessageEncryption = true,
            KeySize = 1024,
            Authenticator = new OidcAuthenticator(KeycloakTokenAcquirer.FromEnvironment()),
            AuthenticationTimeout = 600,
        });

        client.Connect();
        WriteLine("Authenticated. Calling the remote method.");

        var proxy = client.CreateProxy<ISampleService>();
        proxy.SayHello();
        return client;
    }
}

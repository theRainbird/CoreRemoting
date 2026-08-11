using CoreRemoting;
using static Logger<Client>;

class Client
{
    public static IRemotingClient Start()
    {
        WriteLine("Connecting to the server using GitHub authentication.");
        WriteLine("Press Enter to continue.");
        ReadLine();

        // credentials not needed
        var client = new RemotingClient(new()
        {
            ServerHostName = "localhost",
            ServerPort = 9192,
            MessageEncryption = false,
            Authenticator = new GitHubAuthenticator("Ov23liN5TPRuvBAvAwof"),
            AuthenticationTimeout = 600,
        });

        client.Connect();
        WriteLine("Connected. Calling the remote method.");

        var proxy = client.CreateProxy<ISampleService>();
        proxy.SayHello();
        return client;
    }
}
using CoreRemoting;
using static Logger<Client>;

class Client
{
    public static IRemotingClient Start()
    {
        WriteLine("Client: connecting. Use any non-empty user name and password.");

        // login
        Write("Client: what's your user name? ");
        var login = ReadLine();

        // password
        Write("Client: what's your password? ");
        var password = ReadLine();

        var client = new RemotingClient(new()
        {
            ServerHostName = "localhost",
            ServerPort = 9191,
            MessageEncryption = false,
            Authenticator = new TwoFactorAuthenticator(),
            Credentials = [
                new() { Name = "login", Value = login },
                new() { Name = "password", Value = password },
            ]
        });

        client.Connect();
        WriteLine("Client: connected. Calling the remote method.");

        var proxy = client.CreateProxy<ISampleService>();
        proxy.SayHello();
        return client;
    }
}

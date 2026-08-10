using CoreRemoting;
using static Logger<Client>;

// start the server, connect the client, authenticate
using var server = StartServer();

using var client = StartClient();

IRemotingServer StartServer()
{
    var server = new RemotingServer(new()
    {
        HostName = "localhost",
        NetworkPort = 9191,
        MessageEncryption = false,
        AuthenticationProvider = new TwoFactorAuthProvider(),
        RegisterServicesAction = container =>
            container.RegisterService<ISampleService, SampleService>()
    });

    server.Start();
    Logger<Server>.WriteLine("Server: started. Starting client...");
    return server;
}

IRemotingClient StartClient()
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

WriteLine("Program: press Enter to quit.");
ReadLine();


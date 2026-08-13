using CoreRemoting;
using static Logger<Server>;

class Server
{
    public static IRemotingServer Start()
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
        WriteLine("Started. Starting client...");
        return server;
    }
}


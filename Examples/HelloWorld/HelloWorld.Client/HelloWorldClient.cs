using System;
using CoreRemoting;
using CoreRemoting.Serialization.Binary;
using CoreRemoting.Serialization.Bson;
using HelloWorld.Shared;

namespace HelloWorld.Client
{
    /// <summary>
    /// Client application class of hello world chat example.
    /// </summary>
    public static class HelloWorldClient
    {
        /// <summary>
        /// Application entry point.
        /// </summary>
        static void Main()
        {
            // Create and configure new CoreRemoting client 
            using var client = new RemotingClient(new ClientConfig()
            {
                ServerHostName = "localhost",
                Serializer = new BinarySerializerAdapter(),
                MessageEncryption = false,
                ServerPort = 9090
            });
            
            // Establish connection to server
            client.Connect();

            // Creates proxy for remote service
            var proxy = client.CreateProxy<ISayHelloService>();
            
            // Subscribe MessageReceived event of remote service
            proxy.MessageReceived += (senderName, message) => 
                Console.WriteLine($"\n  {senderName} says: {message}\n");
            
            Console.WriteLine("What's your name?");
            var name = Console.ReadLine();

            Console.WriteLine("\nEntered chat. Type 'quit' to leave.");

            bool quit = false;

            while (!quit)
            {
                var text = Console.ReadLine();

                if (text != null && text.Equals("quit", StringComparison.InvariantCultureIgnoreCase))
                    quit = true;
                else
                {
                    // Call remote method
                    proxy.Say(name, text);
                }
            }
        }
    }
}
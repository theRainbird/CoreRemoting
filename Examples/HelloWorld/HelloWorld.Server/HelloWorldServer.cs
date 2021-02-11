using System;
using CoreRemoting;
using CoreRemoting.DependencyInjection;
using HelloWorld.Shared;

namespace HelloWorld.Server
{
    /// <summary>
    /// Hello wordl chat service.
    /// </summary>
    public class SayHelloService : ISayHelloService
    {
        /// <summary>
        /// Event: Fired when a chat message is received.
        /// </summary>
        public event Action<string, string> MessageReceived;
        
        /// <summary>
        /// Say something in the chat.
        /// </summary>
        /// <param name="name">User name</param>
        /// <param name="message">Message to post in chat</param>
        public void Say(string name, string message)
        {
            MessageReceived?.Invoke(name, message);
        }
    }

    /// <summary>
    /// Hello world chat server application class.
    /// </summary>
    public static class HelloWorldServer
    {
        /// <summary>
        /// Server application entry point.
        /// </summary>
        static void Main()
        {
            // Create an configure a CoreRemoting server
            using var server = new RemotingServer(new ServerConfig()
            {
                HostName = "localhost",
                NetworkPort = 9090,
                MaximumSessionInactivityTime = 1,
                InactiveSessionSweepInterval = 10,
                RegisterServicesAction = container =>
                {
                    container.RegisterService<ISayHelloService, SayHelloService>(ServiceLifetime.Singleton);
                }
            });
            
            // Start server
            server.Start();
            
            Console.WriteLine("Server is running.");
            Console.ReadLine();
        }
    }
}
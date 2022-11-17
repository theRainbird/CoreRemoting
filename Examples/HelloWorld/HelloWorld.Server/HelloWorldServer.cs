using System;
using System.Runtime.InteropServices;
using CoreRemoting;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Serialization.Binary;
using CoreRemoting.Serialization.Bson;
using HelloWorld.Shared;

namespace HelloWorld.Server
{
    /// <summary>
    /// Hello world chat service.
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
                MessageEncryption = false,
                Serializer = new BinarySerializerAdapter(),
                RegisterServicesAction = container =>
                {
                    container.RegisterService<ISayHelloService, SayHelloService>(ServiceLifetime.Singleton);
                }
            });

            server.Error += (sender, exception) =>
            {
                Console.WriteLine("--[Error]--------------------------");
                Console.WriteLine(exception.Message);
                Console.WriteLine(exception.StackTrace);
                
                if (exception.InnerException != null)
                {
                    Console.WriteLine(exception.InnerException.Message);
                    Console.WriteLine(exception.InnerException.StackTrace);
                }

                Console.WriteLine("-----------------------------------");
            };

            // Start server
            server.Start();
            
            Console.WriteLine("Server is running.");
            Console.ReadLine();
        }
    }
}
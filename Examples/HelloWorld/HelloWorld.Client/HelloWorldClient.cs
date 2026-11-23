using System;
using System.Threading;
using CoreRemoting;
using CoreRemoting.Serialization.NeoBinary;
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
                Serializer = new NeoBinarySerializerAdapter(), 
                MessageEncryption = true,                  
                ServerPort = 9090,
                // Detect a dead connection quickly (the default is 20 seconds).
                // Auto-reconnect relies on the heartbeat to notice a half-open link.
                KeepSessionAliveInterval = 1,
                // Reconnect in the background with a short exponential backoff.
                ReconnectMinDelay = 1,
                ReconnectMaxDelay = 5
            });

            // Report when the link is back up and whether the session survived.
            client.Reconnected += (sender, e) =>
                Console.WriteLine(
                    $"\n  [Reconnected] session resumed: {e.SessionResumed}, " +
                    $"subscriptions restored: {e.SubscriptionsRestored}\n");

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
            Console.WriteLine(
                "You can kill/restart the server or drop the network - " +
                "the client reconnects and retries sending.\n");

            bool quit = false;

            while (!quit)
            {
                var text = Console.ReadLine();

                if (text != null && text.Equals("quit", StringComparison.InvariantCultureIgnoreCase))
                    quit = true;
                else
                {
                    // Call remote method, transparently retrying while the client
                    // is reconnecting after a network drop or a server restart.
                    SayWithRetry(proxy, name, text);
                }
            }
        }

        /// <summary>
        /// Sends a chat message and retries while the client is reconnecting.
        /// </summary>
        /// <param name="proxy">The remote service proxy.</param>
        /// <param name="name">The user name to send with the message.</param>
        /// <param name="message">The chat message.</param>
        /// <param name="maxAttempts">How many times to retry before giving up.</param>
        /// <remarks>
        /// A single call can fail if it races the exact moment the connection is lost,
        /// or while the server is still down or restarting. The background auto-reconnect
        /// loop re-establishes the link in the meantime, so this short retry loop rides out
        /// the outage instead of crashing the chat.
        /// </remarks>
        private static void SayWithRetry(ISayHelloService proxy, string name, string message, int maxAttempts = 30)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    proxy.Say(name, message);
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt >= maxAttempts)
                    {
                        Console.WriteLine(
                            $"\n  [Error] Message could not be sent after {maxAttempts} attempts: " +
                            $"{ex.GetBaseException().Message}\n");
                        return;
                    }

                    Console.WriteLine(
                        $"\n  [Reconnecting] send failed ({ex.GetBaseException().Message}); " +
                        $"retry {attempt}/{maxAttempts}...\n");

                    Thread.Sleep(1000);
                }
            }
        }
    }
}

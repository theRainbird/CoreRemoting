using System;

namespace HelloWorld.Shared
{
    /// <summary>
    /// Interface of hello world chat service.
    /// </summary>
    public interface ISayHelloService
    {
        /// <summary>
        /// Event: Fired when a chat message is received.
        /// </summary>
        event Action<string, string> MessageReceived;
        
        /// <summary>
        /// Say something in the chat.
        /// </summary>
        /// <param name="name">User name</param>
        /// <param name="message">Message to post in chat</param>
        void Say(string name, string message);
    }
}
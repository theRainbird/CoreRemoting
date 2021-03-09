using System;

namespace CoreRemoting.Channels
{
    /// <summary>
    /// Interface to be implemented by raw message transport components.
    /// </summary>
    public interface IRawMessageTransport
    {
        /// <summary>
        /// Event: Fires when a message is received from server.
        /// </summary>
        event Action<byte[]> ReceiveMessage;

        /// <summary>
        /// Event: Fires when an error is occurred.
        /// </summary>
        event Action<string, Exception> ErrorOccured;
        
        /// <summary>
        /// Gets or sets the last exception.
        /// </summary>
        NetworkException LastException { get; set; }
        
        /// <summary>
        /// Sends a message to the server.
        /// </summary>
        /// <param name="rawMessage">Raw message data</param>
        void SendMessage(byte[] rawMessage);
    }
}
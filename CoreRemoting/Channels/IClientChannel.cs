using System;

namespace CoreRemoting.Channels
{
    /// <summary>
    /// Interface for CoreRemoting client side transport channel.
    /// </summary>
    public interface IClientChannel : IDisposable
    {
        /// <summary>
        /// Initializes the channel.
        /// </summary>
        /// <param name="client">CoreRemoting client</param>
        void Init(IRemotingClient client);
        
        /// <summary>
        /// Establish a connection with the server.
        /// </summary>
        void Connect();

        /// <summary>
        /// Closes the connection.
        /// </summary>
        void Disconnect();
        
        /// <summary>
        /// Gets whether the connection is established or not.
        /// </summary>
        bool IsConnected { get; }
        
        /// <summary>
        /// Gets the raw message transport component for this connection.
        /// </summary>
        IRawMessageTransport RawMessageTransport { get; }
    }
}
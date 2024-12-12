using System;
using System.Threading.Tasks;

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
        Task ConnectAsync();

        /// <summary>
        /// Closes the connection.
        /// </summary>
        Task DisconnectAsync();
        
        /// <summary>
        /// Gets whether the connection is established or not.
        /// </summary>
        bool IsConnected { get; }
        
        /// <summary>
        /// Gets the raw message transport component for this connection.
        /// </summary>
        IRawMessageTransport RawMessageTransport { get; }

        /// <summary>
        /// Event: Fires when the server disconnects.
        /// </summary>
        event Action Disconnected;
    }
}
using System;

namespace CoreRemoting.Channels
{
    /// <summary>
    /// Interface for CoreRemoting server side transport channel.
    /// </summary>
    public interface IServerChannel : IDisposable
    {
        /// <summary>
        /// Initializes the channel.
        /// </summary>
        /// <param name="server">CoreRemoting sever</param>
        void Init(IRemotingServer server);
        
        /// <summary>
        /// Start listening for client requests.
        /// </summary>
        void StartListening();
        
        /// <summary>
        /// Stop listening for client requests.
        /// </summary>
        void StopListening();
        
        /// <summary>
        /// Gets whether the channel is listening or not.
        /// </summary>
        bool IsListening { get; }
    }
}
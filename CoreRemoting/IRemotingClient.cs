using System;

namespace CoreRemoting
{
    /// <summary>
    /// Interface of a CoreRemoting client.
    /// </summary>
    public interface IRemotingClient : IDisposable
    {
        /// <summary>
        /// Gets the configuration settings used by the CoreRemoting client instance.
        /// </summary>
        ClientConfig Config { get; }
        
        /// <summary>
        /// Gets the public key of this CoreRemoting client instance.
        /// </summary>
        byte[] PublicKey { get; }
        
        /// <summary>
        /// Gets or sets the invocation timeout in milliseconds.
        /// </summary>
        int? InvocationTimeout { get; set; }

        /// <summary>
        /// Gets or sets whether messages should be encrypted or not.
        /// </summary>
        bool MessageEncryption { get; }

        /// <summary>
        /// Creates a proxy object to provide access to a remote service.
        /// </summary>
        /// <typeparam name="T">Type of the shared interface of the remote service</typeparam>
        /// <param name="serviceName">Unique name of the remote service</param>
        /// <returns>Proxy object</returns>
        T CreateProxy<T>(string serviceName = "");

        /// <summary>
        /// Creates a proxy object to provide access to a remote service.
        /// </summary>
        /// <param name="serviceInterfaceType">Interface type of the remote service</param>
        /// <param name="serviceName">Unique name of the remote service</param>
        /// <returns>Proxy object</returns>
        object CreateProxy(Type serviceInterfaceType, string serviceName = "");

        /// <summary>
        /// Shuts a specified service proxy down and frees resources.
        /// </summary>
        /// <param name="serviceProxy"></param>
        void ShutdownProxy(object serviceProxy);

        /// <summary>
        /// Connects this CoreRemoting client instance to the configured CoreRemoting server.
        /// </summary>
        void Connect();

        /// <summary>
        /// Disconnects from the server. The server is actively notified about disconnection.
        /// </summary>
        void Disconnect();

        /// <summary>
        /// Gets whether the connection to the server is established or not.
        /// </summary>
        bool IsConnected { get; }
        
        /// <summary>
        /// Gets whether this CoreRemoting client instance has a session or not.
        /// </summary>
        bool HasSession { get; }
    }
}
using System;

namespace CoreRemoting
{
    public interface IRemotingClient : IDisposable
    {
        ClientConfig Config { get; }
        
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
        /// <returns>Proxy object</returns>
        T CreateProxy<T>();

        /// <summary>
        /// Creates a proxy object to provide access to a remote service.
        /// </summary>
        /// <param name="serviceInterfaceType">Interface type of the remote service</param>
        /// <returns>Proxy object</returns>
        object CreateProxy(Type serviceInterfaceType);

        /// <summary>
        /// Shuts a specified service proxy down and frees ressources.
        /// </summary>
        /// <param name="serviceProxy"></param>
        void ShutdownProxy(object serviceProxy);

        void Connect();

        void Disconnect();

        bool IsConnected { get; }
        
        bool HasSession { get; }
    }
}
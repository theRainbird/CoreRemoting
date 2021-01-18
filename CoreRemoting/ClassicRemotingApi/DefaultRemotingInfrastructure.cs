using System;
using System.Diagnostics.CodeAnalysis;

namespace CoreRemoting.ClassicRemotingApi
{
    /// <summary>
    /// Provides default server and client, needed to support classic .NET Remoting API. 
    /// </summary>
    public static class DefaultRemotingInfrastructure
    {
        private static WeakReference<IRemotingClient> _defaultRemotingClientRef;
        private static WeakReference<IRemotingServer> _defaultRemotingServerRef;

        /// <summary>
        /// Gets or sets the default CoreRemoting client.
        /// </summary>
        [SuppressMessage("ReSharper", "ArrangeAccessorOwnerBody")]
        public static IRemotingClient DefaultRemotingClient
        {
            get
            {
                if (_defaultRemotingClientRef == null)
                    return null;

                _defaultRemotingClientRef.TryGetTarget(out var defaultClient);

                return defaultClient;
            }
            internal set
            {
                _defaultRemotingClientRef = 
                    value == null 
                        ? null 
                        : new WeakReference<IRemotingClient>(value);
            }
        }
        
        /// <summary>
        /// Gets or sets the default CoreRemoting server.
        /// </summary>
        [SuppressMessage("ReSharper", "ArrangeAccessorOwnerBody")]
        public static IRemotingServer DefaultRemotingServer
        {
            get
            {
                if (_defaultRemotingServerRef == null)
                    return null;

                _defaultRemotingServerRef.TryGetTarget(out var defaultServer);

                return defaultServer;
            }
            internal set
            {
                _defaultRemotingServerRef = 
                    value == null 
                        ? null 
                        : new WeakReference<IRemotingServer>(value);
            }
        }
       
        /// <summary>
        /// Gets or sets the default client configuration. 
        /// </summary>
        public static ClientConfig DefaultClientConfig { get; set; }
        
        /// <summary>
        /// Gets or sets the default server configuration.
        /// </summary>
        public static ServerConfig DefaultServerConfig { get; set; }
    }
}
using System;
using System.Diagnostics.CodeAnalysis;
using CoreRemoting.Authentication;
using CoreRemoting.Channels;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Serialization;

namespace CoreRemoting
{
    /// <summary>
    /// Describes the configuration settings of a CoreRemoting service instance.
    /// </summary>
    [SuppressMessage("ReSharper", "RedundantDefaultMemberInitializer")]
    public class ServerConfig
    {
        /// <summary>
        /// Creates new new instance of the ServerConfig class.
        /// </summary>
        public ServerConfig()
        {
            UniqueServerInstanceName = Guid.NewGuid().ToString();
        }
        
        /// <summary>
        /// Gets or sets the host name.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
        public string HostName { get; set; } = "localhost";

        /// <summary>
        /// Gets or sets the network port on which the server should be listening for requests.
        /// </summary>
        public int NetworkPort { get; set; } = 9090;

        /// <summary>
        /// Gets or sets the key size for asymmetric encryption (only relevant, if message encryption is enabled).
        /// </summary>
        [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")] 
        public int KeySize { get; set; } = 4096;
        
        /// <summary>
        /// Gets or sets whether messages should be encrypted or not.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")] 
        public bool MessageEncryption { get; set; } = true;
        
        /// <summary>
        /// Gets or sets the serializer to be used (Bson serializer is used, if set to null).
        /// </summary>
        public ISerializerAdapter Serializer { get; set; }

        /// <summary>
        /// Gets or sets the dependency injection container to be used for service registration.
        /// Castle Windsor Container is used, if set to null.
        /// </summary>
        public IDependencyInjectionContainer DependencyInjectionContainer { get; set; }

        /// <summary>
        /// Gets or sets an optional action which should be called on server startup to register services.
        /// </summary>
        public Action<IDependencyInjectionContainer> RegisterServicesAction { get; set; }
        
        /// <summary>
        /// Gets or sets the session repository to be used to manage sessions.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")] 
        public ISessionRepository SessionRepository { get; set; }
        
        /// <summary>
        /// Gets or sets the server channel to be used for transport of messages over the wire (WebsocketServerChannel is used, if set to null).
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")] 
        public IServerChannel Channel { get; set; }
        
        /// <summary>
        /// Gets or sets whether authentication is required in order to establish a new session.
        /// </summary>
        public bool AuthenticationRequired { get; set; } = false;
        
        /// <summary>
        /// Gets or sets the authentication provider that should be used to handle authentication requests.
        /// </summary>
        public IAuthenticationProvider AuthenticationProvider { get; set; }
        
        /// <summary>
        /// Gets or sets the unique name of this server instance.
        /// </summary>
        public string UniqueServerInstanceName { get; set; }

        /// <summary>
        /// Gets or sets the sweep interval for inactive sessions in seconds (No session sweeping if set to 0).
        /// </summary>
        public int InactiveSessionSweepInterval { get; set; } = 60;

        /// <summary>
        /// Gets or sets the maximum session inactivity time in minutes.
        /// </summary>
        public int MaximumSessionInactivityTime { get; set; } = 30;
        
        /// <summary>
        /// Gets or set whether this is the default server.
        /// </summary>
        public bool IsDefault { get; set; } = false;
    }
}
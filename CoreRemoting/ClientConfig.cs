using System.Diagnostics.CodeAnalysis;
using CoreRemoting.Authentication;
using CoreRemoting.Channels;
using CoreRemoting.Serialization;

namespace CoreRemoting
{
    /// <summary>
    /// Provides configuration settings for a CoreRemoting client instance.
    /// </summary>
    public class ClientConfig
    {
        /// <summary>
        /// Gets or sets the connection timeout in seconds (0 means infinite).
        /// </summary>
        public int ConnectionTimeout { get; set; } = 10;
        
        /// <summary>
        /// Gets or sets the authentication timeout in seconds (0 means infinite).
        /// </summary>
        public int AuthenticationTimeout { get; set; } = 10;

        /// <summary>
        /// Gets or sets the invocation timeout in seconds (0 means infinite).
        /// </summary>
        public int InvocationTimeout { get; set; }

        /// <summary>
        /// Gets or sets the host name of the CoreRemoting server to be connected to.
        /// </summary>
        public string ServerHostName { get; set; } = "localhost";

        /// <summary>
        /// Gets or sets the network port of the CoreRemoting server to be connected to.
        /// </summary>
        public int ServerPort { get; set; } = 9090;
        
        /// <summary>
        /// Gets or sets the serializer to be used (Binary serializer is used, if set to null).
        /// </summary>
        public ISerializerAdapter Serializer { get; set; }

        /// <summary>
        /// Gets or sets the key size for assymetric encryption (only relevant, if message encryption is enabled).
        /// </summary>
        public int KeySize { get; set; } = 4096;

        /// <summary>
        /// Gets or sets whether messages should be encrypted or not.
        /// </summary>
        public bool MessageEncryption { get; set; }
        
        /// <summary>
        /// Gets or sets the client channel to be used for transport of messages over the wire (WebsocketClientChannel is used, if set to null).
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
        public IClientChannel Channel { get; set; }
        
        /// <summary>
        /// Gets or sets the known type provider (only relevant if the serializer needs known types specified).
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
        public IKnownTypeProvider KnownTypeProvider { get; set; }
        
        /// <summary>
        /// Gets or sets an array of credentials for authentication (depends on the authentication provider used on server side). 
        /// </summary>
        public Credential[] Credentials { get; set; }

        /// <summary>
        /// Gets or sets an interval in seconds to keep session alive, even on idle (session is not kept alive if set to 0).
        /// </summary>
        public int KeepSessionAliveInterval { get; set; } = 20;
    }
}
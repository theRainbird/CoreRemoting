using System;
using CoreRemoting.Authentication;
using CoreRemoting.DependencyInjection;
using CoreRemoting.RpcMessaging;
using CoreRemoting.Serialization;

namespace CoreRemoting
{
    /// <summary>
    /// Interface of a CoreRemoting server.
    /// </summary>
    public interface IRemotingServer : IDisposable
    {
        /// <summary>
        /// Event: Fires before an RPC call is invoked.
        /// </summary>
        event EventHandler<ServerRpcContext> BeforeCall;
        
        /// <summary>
        /// Event: Fires after an RPC call is invoked.
        /// </summary>
        event EventHandler<ServerRpcContext> AfterCall;

        /// <summary>
        /// Event: Fires if an error occurs.
        /// </summary>
        event EventHandler<Exception> Error;
        
        /// <summary>
        /// Gets the unique name of this server instance.
        /// </summary>
        string UniqueServerInstanceName { get; }
        
        /// <summary>
        /// Gets the dependency injection container that is used a service registry.
        /// </summary>
        IDependencyInjectionContainer ServiceRegistry { get; }
        
        /// <summary>
        /// Gets the configured serializer.
        /// </summary>
        ISerializerAdapter Serializer { get; }
        
        /// <summary>
        /// Gets the component for easy building of method call messages.
        /// </summary>
        MethodCallMessageBuilder MethodCallMessageBuilder { get; }
        
        /// <summary>
        /// Gets the component for encryption and decryption of messages.
        /// </summary>
        IMessageEncryptionManager MessageEncryptionManager { get; }
        
        /// <summary>
        /// Gets the session repository to perform session management tasks.
        /// </summary>
        ISessionRepository SessionRepository { get; }
        
        /// <summary>
        /// Gets the configuration settings.
        /// </summary>
        ServerConfig Config { get; }
        
        /// <summary>
        /// Starts listening for client requests.
        /// </summary>
        void Start();
        
        /// <summary>
        /// Stops listening for client requests and close all open client connections.
        /// </summary>
        void Stop();

        /// <summary>
        /// Authenticates the specified credentials and returns whether the authentication was successful or not.
        /// </summary>
        /// <param name="credentials">Credentials to be used for authentication</param>
        /// <param name="authenticatedIdentity">Authenticated identity (null when authentication fails)</param>
        /// <returns>True when authentication was successful, otherwise false</returns>
        bool Authenticate(Credential[] credentials, out RemotingIdentity authenticatedIdentity);
    }
}
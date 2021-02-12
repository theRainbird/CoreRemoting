using System;
using System.Diagnostics.CodeAnalysis;
using CoreRemoting.Authentication;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Websocket;
using CoreRemoting.ClassicRemotingApi;
using CoreRemoting.DependencyInjection;
using CoreRemoting.RpcMessaging;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.Serialization;
using CoreRemoting.Serialization.Bson;
using ServiceLifetime = CoreRemoting.DependencyInjection.ServiceLifetime;

namespace CoreRemoting
{
    /// <summary>
    /// CoreRemoting server implementation.
    /// </summary>
    public sealed class RemotingServer : IRemotingServer
    {
        private readonly IDependencyInjectionContainer _container;
        private readonly ServerConfig _config;
        private readonly string _uniqueServerInstanceName;

        /// <summary>
        /// Creates a new instance of the RemotingServer class.
        /// </summary>
        /// <param name="config">Configuration settings to be used (Default configuration is used, if left null)</param>
        public RemotingServer(ServerConfig config = null)
        {
            _config = config ?? new ServerConfig();

            _uniqueServerInstanceName = 
                string.IsNullOrWhiteSpace(_config.UniqueServerInstanceName) 
                    ? Guid.NewGuid().ToString() 
                    : _config.UniqueServerInstanceName;
                
            RemotingConfiguration.RegisterServer(this);
            
            SessionRepository = 
                _config.SessionRepository ?? 
                    new SessionRepository( 
                        keySize: _config.KeySize,
                        inactiveSessionSweepInterval: _config.InactiveSessionSweepInterval,
                        maximumSessionInactivityTime: _config.MaximumSessionInactivityTime);
            
            _container = _config.DependencyInjectionContainer ?? new CastleWindsorDependencyInjectionContainer();
            Serializer = _config.Serializer ?? new BsonSerializerAdapter();
            MethodCallMessageBuilder = new MethodCallMessageBuilder();
            MessageEncryptionManager = new MessageEncryptionManager();
            
            _container.RegisterService<IDelegateProxyFactory, DelegateProxyFactory>(
                lifetime: ServiceLifetime.Singleton);
            
            _config.RegisterServicesAction?.Invoke(_container);

            Channel = _config.Channel ?? new WebsocketServerChannel();
            
            Channel.Init(this);
            
            if (string.IsNullOrWhiteSpace(_config.UniqueServerInstanceName))
                DefaultRemotingInfrastructure.DefaultRemotingServer ??= this;
        }
        
        /// <summary>
        /// Event: Fires before an RPC call is invoked.
        /// </summary>
        public event EventHandler<ServerRpcContext> BeforeCall;
        
        /// <summary>
        /// Event: Fires after an RPC call is invoked.
        /// </summary>
        public event EventHandler<ServerRpcContext> AfterCall;

        /// <summary>
        /// Event: Fires if an error occures.
        /// </summary>
        public event EventHandler<Exception> Error;
        
        /// <summary>
        /// Gets the dependency injection container that is used a service registry.
        /// </summary>
        public IDependencyInjectionContainer ServiceRegistry => _container;

        /// <summary>
        /// Gets the unique name of this server instance.
        /// </summary>
        public string UniqueServerInstanceName => _uniqueServerInstanceName;

        /// <summary>
        /// Gets the configuration settings.
        /// </summary>
        public ServerConfig Config => _config;
        
        /// <summary>
        /// Gets the configured serializer.
        /// </summary>
        public ISerializerAdapter Serializer { get; }

        /// <summary>
        /// Gets the component for easy building of method call messages.
        /// </summary>
        public MethodCallMessageBuilder MethodCallMessageBuilder { get; }

        /// <summary>
        /// Gets the component for encryption and decryption of messages.
        /// </summary>
        public IMessageEncryptionManager MessageEncryptionManager { get; }
        
        /// <summary>
        /// Gets the session repository to perform session management tasks.
        /// </summary>
        public ISessionRepository SessionRepository { get; }

        /// <summary>
        /// Gets the channel used to do the raw network transport.
        /// </summary>
        [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
        public IServerChannel Channel { get; private set; }

        /// <summary>
        /// Fires the OnBeforeCall event.
        /// </summary>
        /// <param name="serverRpcContext">Server side RPC call context</param>
        internal void OnBeforeCall(ServerRpcContext serverRpcContext)
        {
            BeforeCall?.Invoke(this, serverRpcContext);
        }

        /// <summary>
        /// Fires the OnAfterCall event.
        /// </summary>
        /// <param name="serverRpcContext">Server side RPC call context</param>
        internal void OnAfterCall(ServerRpcContext serverRpcContext)
        {
            AfterCall?.Invoke(this, serverRpcContext);
        }

        /// <summary>
        /// Fires the OnError event.
        /// </summary>
        /// <param name="ex">Exception that describes the occured error</param>
        internal void OnError(Exception ex)
        {
            Error?.Invoke(this, ex);
        }
        
        /// <summary>
        /// Starts listening for client requests.
        /// </summary>
        public void Start()
        {
            Channel.StartListening();
        }

        /// <summary>
        /// Stops listening for client requests and close all open client connections.
        /// </summary>
        public void Stop()
        {
            Channel.StopListening();
        }

        /// <summary>
        /// Authenticates the specified credentials and returns whether the authentication was successful or not.
        /// </summary>
        /// <param name="credentials">Credentials to be used for authentication</param>
        /// <param name="authenticatedIdentity">Authenticated identity (null when authentication fails)</param>
        /// <returns>True when authentication was successful, otherwise false</returns>
        public bool Authenticate(Credential[] credentials, out RemotingIdentity authenticatedIdentity)
        {
            if (_config.AuthenticationProvider == null)
            {
                authenticatedIdentity = null;
                return false;
            }

            return _config.AuthenticationProvider.Authenticate(credentials, out authenticatedIdentity);
        }

        /// <summary>
        /// Frees managed resources.
        /// </summary>
        public void Dispose()
        {
            RemotingConfiguration.UnregisterServer(this);
            
            if (Channel != null)
            {
                Channel.Dispose();
                Channel = null;
            }
        }
    }
}
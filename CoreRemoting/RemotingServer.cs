using System;
using CoreRemoting.Authentication;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Websocket;
using CoreRemoting.ClassicRemotingApi;
using CoreRemoting.DependencyInjection;
using CoreRemoting.RpcMessaging;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.Serialization;
using CoreRemoting.Serialization.Binary;
using ServiceLifetime = CoreRemoting.DependencyInjection.ServiceLifetime;

namespace CoreRemoting
{
    public sealed class RemotingServer : IRemotingServer
    {
        private readonly IDependencyInjectionContainer _container;
        private readonly ServerConfig _config;

        public RemotingServer() : this(DefaultRemotingInfrastructure.Singleton.DefaultServerConfig)
        {
        }
        
        public RemotingServer(ServerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
                
            SessionRepository = _config.SessionRepository ?? new SessionRepository(config.KeySize);
            _container = _config.DependencyInjectionContainer ?? new CastleWindsorDependencyInjectionContainer();
            Serializer = _config.Serializer ?? new BinarySerializerAdapter();
            MethodCallMethodCallMessageBuilder = new MethodCallMethodCallMessageBuilder();
            MessageEncryptionManager = new MessageEncryptionManager();
            KnownTypeProvider = config.KnownTypeProvider ?? new KnownTypeProvider();
            
            _container.RegisterService<IDelegateProxyFactory, DelegateProxyFactory>(
                lifetime: ServiceLifetime.Singleton);
            
            _config.RegisterServicesAction?.Invoke(_container);

            Channel = config.Channel ?? new WebsocketServerChannel();
            
            Channel.Init(this);
            
            var defaultInfrastructure = DefaultRemotingInfrastructure.Singleton;
            
            if (config == defaultInfrastructure.DefaultServerConfig)
                DefaultRemotingInfrastructure.Singleton.DefaultRemotingServer ??= this;
        }
        
        public event EventHandler<ServerRpcContext> BeforeCall;
        
        public event EventHandler<ServerRpcContext> AfterCall;

        public event EventHandler<Exception> Error;
        
        public IDependencyInjectionContainer ServiceRegistry => _container;

        public ServerConfig Config => _config;
        
        public ISerializerAdapter Serializer { get; }

        public MethodCallMethodCallMessageBuilder MethodCallMethodCallMessageBuilder { get; }

        public IMessageEncryptionManager MessageEncryptionManager { get; }
        
        public ISessionRepository SessionRepository { get; }

        public IServerChannel Channel { get; private set; }

        public IKnownTypeProvider KnownTypeProvider { get; }
        
        internal void OnBeforeCall(ServerRpcContext serverRpcContext)
        {
            BeforeCall?.Invoke(this, serverRpcContext);
        }

        internal void OnAfterCall(ServerRpcContext serverRpcContext)
        {
            AfterCall?.Invoke(this, serverRpcContext);
        }

        internal void OnError(Exception ex)
        {
            Error?.Invoke(this, ex);
        }
        
        public void Start()
        {
            Channel.StartListening();
        }

        public void Stop()
        {
            Channel.StopListening();
        }

        public bool Authenticate(Credential[] credentials, out RemotingIdentity authenticatedIdentity)
        {
            if (_config.AuthenticationProvider == null)
            {
                authenticatedIdentity = null;
                return false;
            }

            return _config.AuthenticationProvider.Authenticate(credentials, out authenticatedIdentity);
        }

        public void Dispose()
        {
            if (Channel != null)
            {
                Channel.Dispose();
                Channel = null;
            }
        }
    }
}
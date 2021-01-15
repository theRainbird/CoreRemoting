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
        private readonly string _uniqueServerInstanceName;

        public RemotingServer(ServerConfig config = null)
        {
            _config = config ?? new ServerConfig();

            _uniqueServerInstanceName = 
                string.IsNullOrWhiteSpace(_config.UniqueServerInstanceName) 
                    ? Guid.NewGuid().ToString() 
                    : _config.UniqueServerInstanceName;
                
            RemotingConfiguration.RegisterServer(this);
            
            SessionRepository = _config.SessionRepository ?? new SessionRepository(_config.KeySize);
            _container = _config.DependencyInjectionContainer ?? new CastleWindsorDependencyInjectionContainer();
            Serializer = _config.Serializer ?? new BinarySerializerAdapter();
            MethodCallMethodCallMessageBuilder = new MethodCallMethodCallMessageBuilder();
            MessageEncryptionManager = new MessageEncryptionManager();
            KnownTypeProvider = _config.KnownTypeProvider ?? new KnownTypeProvider();
            
            _container.RegisterService<IDelegateProxyFactory, DelegateProxyFactory>(
                lifetime: ServiceLifetime.Singleton);
            
            _config.RegisterServicesAction?.Invoke(_container);

            Channel = _config.Channel ?? new WebsocketServerChannel();
            
            Channel.Init(this);
            
            if (string.IsNullOrWhiteSpace(_config.UniqueServerInstanceName))
                DefaultRemotingInfrastructure.DefaultRemotingServer ??= this;
        }
        
        public event EventHandler<ServerRpcContext> BeforeCall;
        
        public event EventHandler<ServerRpcContext> AfterCall;

        public event EventHandler<Exception> Error;
        
        public IDependencyInjectionContainer ServiceRegistry => _container;

        public string UniqueServerInstanceName => _uniqueServerInstanceName;

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
            RemotingConfiguration.UnregisterServer(this);
            
            if (Channel != null)
            {
                Channel.Dispose();
                Channel = null;
            }
        }
    }
}
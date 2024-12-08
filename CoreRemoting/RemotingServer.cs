using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CoreRemoting.Authentication;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Tcp;
using CoreRemoting.DependencyInjection;
using CoreRemoting.RpcMessaging;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.Serialization;
using CoreRemoting.Serialization.Bson;
using ServiceLifetime = CoreRemoting.DependencyInjection.ServiceLifetime;
using System.Runtime.ExceptionServices;

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

        // ReSharper disable once InconsistentNaming
        private static readonly ConcurrentDictionary<string, IRemotingServer> _serverInstances =
            new ConcurrentDictionary<string, IRemotingServer>();

        private static WeakReference<IRemotingServer> _defaultRemotingServerRef;

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

            _serverInstances.AddOrUpdate(
                key: _config.UniqueServerInstanceName,
                addValueFactory: _ => this,
                updateValueFactory: (_, oldServer) =>
                {
                    oldServer?.Dispose();
                    return this;
                });

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
                lifetime: ServiceLifetime.Singleton,
                asHiddenSystemService: true);

            _config.RegisterServicesAction?.Invoke(_container);

            Channel = _config.Channel ?? new TcpServerChannel();

            Channel.Init(this);

            if (_config.IsDefault)
                RemotingServer.DefaultRemotingServer ??= this;
        }

        /// <summary>
        /// Event: Fires when a client logs on.
        /// </summary>
        public event EventHandler Logon;

        /// <summary>
        /// Event: Fires when a client logs off.
        /// </summary>
        public event EventHandler Logoff;

        /// <summary>
        /// Event: Fires when an RPC call is prepared and can be canceled.
        /// </summary>
        public event EventHandler<ServerRpcContext> BeginCall;

        /// <summary>
        /// Event: Fires when an RPC call is rejected before BeforeCall event.
        /// </summary>
        public event EventHandler<ServerRpcContext> RejectCall;

        /// <summary>
        /// Event: Fires before an RPC call is invoked.
        /// </summary>
        public event EventHandler<ServerRpcContext> BeforeCall;

        /// <summary>
        /// Event: Fires after an RPC call is invoked.
        /// </summary>
        public event EventHandler<ServerRpcContext> AfterCall;

        /// <summary>
        /// Event: Fires if an error occurs.
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
        /// Fires the <see cref="BeforeCall"/> event.
        /// </summary>
        /// <param name="serverRpcContext">Server side RPC call context</param>
        internal void OnBeforeCall(ServerRpcContext serverRpcContext)
        {
            BeforeCall?.Invoke(this, serverRpcContext);
        }

        /// <summary>
        /// Fires the <see cref="AfterCall"/> event.
        /// </summary>
        /// <param name="serverRpcContext">Server side RPC call context</param>
        internal void OnAfterCall(ServerRpcContext serverRpcContext)
        {
            AfterCall?.Invoke(this, serverRpcContext);
        }

        /// <summary>
        /// Fires the <see cref="Error"/> event.
        /// </summary>
        /// <param name="ex">Exception that describes the occurred error</param>
        internal void OnError(Exception ex)
        {
            Error?.Invoke(this, ex);
        }

        /// <summary>
        /// Fires the <see cref="RejectCall"/> event.
        /// </summary>
        /// <param name="serverRpcContext">Server side RPC call context</param>
        internal void OnRejectCall(ServerRpcContext serverRpcContext)
        {
            RejectCall?.Invoke(this, serverRpcContext);
        }

        /// <summary>
        /// Fires the <see cref="BeginCall"/> event.
        /// </summary>
        /// <param name="serverRpcContext">Server side RPC call context</param>
        internal void OnBeginCall(ServerRpcContext serverRpcContext)
        {
            BeginCall?.Invoke(this, serverRpcContext);

            if (serverRpcContext.Cancel)
            {
                var cancelEx = serverRpcContext.Exception ??
                    new RemoteInvocationException($"Invocation canceled: {
                        serverRpcContext.MethodCallMessage.ServiceName}.{
                        serverRpcContext.MethodCallMessage.MethodName}");

                // rethrow the exception keeping the original stack trace
                ExceptionDispatchInfo.Capture(cancelEx).Throw();
            }
        }

        /// <summary>
        /// Fires the <see cref="Logon"/> event.
        /// </summary>
        internal void OnLogon()
        {
            Logon?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Fires the <see cref="Logoff"/> event.
        /// </summary>
        internal void OnLogoff()
        {
            Logoff?.Invoke(this, EventArgs.Empty);
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
            authenticatedIdentity = null;

            if (_config.AuthenticationProvider == null)
                return false;

            try
            {
                return _config.AuthenticationProvider.Authenticate(credentials, out authenticatedIdentity);
            }
            catch (Exception ex)
            {
                OnError(ex);

                return false;
            }
        }

        /// <summary>
        /// Frees managed resources.
        /// </summary>
        public void Dispose()
        {
            if (RemotingServer.DefaultRemotingServer == this)
                RemotingServer.DefaultRemotingServer = null;

            _serverInstances.TryRemove(_config.UniqueServerInstanceName, out _);

            if (Channel != null)
            {
                Channel.Dispose();
                Channel = null;
            }
        }

        #region Managing server instances

        /// <summary>
        /// Gets a list of active server instances.
        /// </summary>
        public static IEnumerable<IRemotingServer> ActiveServerInstances => _serverInstances.Values;

        /// <summary>
        /// Gets a active server instance by its unique instance name.
        /// </summary>
        /// <param name="uniqueServerInstanceName">Unique server instance name</param>
        /// <returns>Active CoreRemoting server</returns>
        public static IRemotingServer GetActiveServerInstance(string uniqueServerInstanceName)
        {
            _serverInstances.TryGetValue(uniqueServerInstanceName, out var server);
            return server;
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

        #endregion
    }
}
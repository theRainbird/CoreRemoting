using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Threading;
using Castle.DynamicProxy;
using CoreRemoting.Authentication;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Websocket;
using CoreRemoting.ClassicRemotingApi;
using CoreRemoting.RpcMessaging;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.Encryption;
using CoreRemoting.Serialization;
using CoreRemoting.Serialization.Binary;

namespace CoreRemoting
{
    /// <summary>
    /// Provides remoting functionality on client side.
    /// </summary>
    public sealed class RemotingClient : IRemotingClient
    {
        #region Fields
        
        private IClientChannel _channel;
        private IRawMessageTransport _rawMessageTransport;
        private readonly ProxyGenerator _proxyGenerator;
        private readonly RsaKeyPair _keyPair;
        private readonly ClientDelegateRegistry _delegateRegistry;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly ClientConfig _config;
        private readonly ConcurrentDictionary<Guid, ClientRpcContext> _activeCalls;
        private Guid _sessionId;
        private ManualResetEventSlim _handshakeCompletedWaitHandle;
        private ManualResetEventSlim _authenticationCompletedWaitHandle;
        private ManualResetEventSlim _goodbyeCompletedWaitHandle;
        private bool _isAuthenticated;
        
        #endregion
        
        #region Construction
        
        private RemotingClient()
        {
            MethodCallMessageBuilder = new MethodCallMethodCallMessageBuilder();
            MessageEncryptionManager = new MessageEncryptionManager();
            _proxyGenerator = new ProxyGenerator();
            _activeCalls = new ConcurrentDictionary<Guid, ClientRpcContext>();
            _cancellationTokenSource = new CancellationTokenSource();
            _delegateRegistry = new ClientDelegateRegistry();
            _handshakeCompletedWaitHandle = new ManualResetEventSlim(initialState: false);
            _authenticationCompletedWaitHandle = new ManualResetEventSlim(initialState: false);
            _goodbyeCompletedWaitHandle = new ManualResetEventSlim(initialState: false);
        }
        
        /// <summary>
        /// Creates a new instance of the RemotingClient class.
        /// </summary>
        /// <param name="config">Configuration settings</param>
        public RemotingClient(ClientConfig config = null) : this()
        {
            config ??= DefaultRemotingInfrastructure.DefaultClientConfig;

            if (config == null)
                throw new ArgumentException("No config provided and no default configuration found.");
            
            Serializer = config.Serializer ?? new BinarySerializerAdapter();
            KnownTypeProvider = config.KnownTypeProvider ?? new KnownTypeProvider();
            MessageEncryption = config.MessageEncryption;
            
            _config = config;
            
            if (MessageEncryption)
                _keyPair = new RsaKeyPair(config.KeySize);
            
            _channel = config.Channel ?? new WebsocketClientChannel();
            
            _channel.Init(this);
            _rawMessageTransport = _channel.RawMessageTransport;
            _rawMessageTransport.ReceiveMessage += OnMessage;
            _rawMessageTransport.ErrorOccured += (s, exception) =>
            {
                if (exception != null)
                    throw exception;

                throw new NetworkException(s);
            };

            if (config == DefaultRemotingInfrastructure.DefaultClientConfig)
                DefaultRemotingInfrastructure.DefaultRemotingClient ??= this;
        }

        #endregion
        
        #region Properties
        
        /// <summary>
        /// Gets a utility object for building remoting messages.
        /// </summary>
        internal IMethodCallMessageBuilder MethodCallMessageBuilder { get; }

        internal IKnownTypeProvider KnownTypeProvider { get; }
        
        /// <summary>
        /// Gets a utility object tp provide encryption of remoting messages.
        /// </summary>
        private IMessageEncryptionManager MessageEncryptionManager { get; }
        
        /// <summary>
        /// Gets the configured serializer.
        /// </summary>
        internal ISerializerAdapter Serializer { get; }

        /// <summary>
        /// Gets the local client delegate registry.
        /// </summary>
        internal ClientDelegateRegistry ClientDelegateRegistry => _delegateRegistry;
        
        /// <summary>
        /// Gets or sets the invocation timeout in milliseconds.
        /// </summary>
        public int? InvocationTimeout { get; set; }
        
        /// <summary>
        /// Gets or sets whether messages should be encrypted or not.
        /// </summary>
        public bool MessageEncryption { get; private set; }

        public ClientConfig Config => _config;

        public byte[] PublicKey => _keyPair.PublicKey;

        public bool IsConnected => _channel?.IsConnected ?? false;

        public bool HasSession => _sessionId != Guid.Empty;
        
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")] 
        public RemotingIdentity Identity { get; private set; }
        
        #endregion
        
        #region Connection management
        
        public void Connect()
        {
            if (_channel == null)
                throw new RemotingException("No client channel configured.");
            
            _channel.Connect();

            if (_channel.RawMessageTransport.LastException != null)
                throw _channel.RawMessageTransport.LastException;
            
            _handshakeCompletedWaitHandle.Wait(_config.ConnectionTimeout * 1000);

            if (!_handshakeCompletedWaitHandle.IsSet)
                throw new NetworkException("Handshake with server failed.");
            else
                Authenticate();
        }

        public void Disconnect()
        {
            if (_channel != null && HasSession)
            {
                byte[] sharedSecret =
                    MessageEncryption
                        ? _sessionId.ToByteArray()
                        : null;

                var goodbyeMessage =
                    new GoodbyeMessage()
                    {
                        SessionId = _sessionId
                    };

                var wireMessage =
                    MessageEncryptionManager.CreateWireMessage(
                        messageType: "goodbye",
                        serializedMessage: Serializer.Serialize(goodbyeMessage),
                        sharedSecret: sharedSecret);

                byte[] rawData = Serializer.Serialize(wireMessage);
                
                _goodbyeCompletedWaitHandle.Reset();
                
                _channel.RawMessageTransport.SendMessage(rawData);

                _goodbyeCompletedWaitHandle.Wait(_config.ConnectionTimeout * 1000);
            }

            _channel?.Disconnect();
            _handshakeCompletedWaitHandle.Reset();
            _authenticationCompletedWaitHandle.Reset();
            Identity = null;
        }

        #endregion
        
        #region Authentication

        private void Authenticate()
        {
            if (_config.Credentials == null || (_config.Credentials!=null && _config.Credentials.Length == 0))
                return;
            
            if (_authenticationCompletedWaitHandle.IsSet)
                return;
            
            byte[] sharedSecret =
                MessageEncryption
                    ? _sessionId.ToByteArray()
                    : null;

            var authRequestMessage =
                new AuthenticationRequestMessage()
                {
                    Credentials = _config.Credentials
                };

            var wireMessage =
                MessageEncryptionManager.CreateWireMessage(
                    messageType: "auth",
                    serializedMessage: Serializer.Serialize(authRequestMessage),
                    sharedSecret: sharedSecret);

            byte[] rawData = Serializer.Serialize(wireMessage);

            _rawMessageTransport.LastException = null;
            
            _rawMessageTransport.SendMessage(rawData);
            
            if (_rawMessageTransport.LastException != null)
                throw _rawMessageTransport.LastException;

            _authenticationCompletedWaitHandle.Wait(_config.AuthenticationTimeout * 1000);
            
            if (!_authenticationCompletedWaitHandle.IsSet)
                throw new SecurityException("Authentication timeout.");

            if (!_isAuthenticated)
                throw new SecurityException("Authentication failed. Please check credentials.");
        }

        #endregion
        
        #region Handling received messages
        
        private void OnMessage(byte[] rawMessage)
        {
            var message = Serializer.Deserialize<WireMessage>(rawMessage);
            
            switch (message.MessageType.ToLower())
            {
                case "complete_handshake":
                    ProcessCompleteHandshakeMessage(message);
                    break;
                case "auth_response":
                    ProcessAuthenticationResponseMessage(message);
                    break;
                case "rpc_result":
                    ProcessRpcResultMessage(message);
                    break;
                case "invoke":
                    ProcessRemoteDelegateInvocationMessage(message);
                    break;
                case "goodbye":
                    _goodbyeCompletedWaitHandle.Set();
                    break;
            }
        }

        private void ProcessCompleteHandshakeMessage(WireMessage message)
        {
            if (MessageEncryption)
            {
                var encryptedSecret =
                    Serializer.Deserialize<EncryptedSecret>(
                        message.Data);

                _sessionId =
                    new Guid(
                        RsaKeyExchange.DecrpytSecret(
                            keySize: _config.KeySize,
                            receiversPrivateKeyBlob: _keyPair.PrivateKey,
                            encryptedSecret: encryptedSecret));
            }
            else
                _sessionId = new Guid(message.Data);

            _handshakeCompletedWaitHandle.Set();
        }

        private void ProcessAuthenticationResponseMessage(WireMessage message)
        {
            byte[] sharedSecret =
                MessageEncryption
                    ? _sessionId.ToByteArray()
                    : null;
            
            var authResponseMessage =
                Serializer
                    .Deserialize<AuthenticationResponseMessage>(
                        MessageEncryptionManager.GetDecryptedMessageData(
                            message: message,
                            sharedSecret: sharedSecret));

            _isAuthenticated = authResponseMessage.IsAuthenticated;

            Identity = _isAuthenticated ? authResponseMessage.AuthenticatedIdentity : null;
            
            _authenticationCompletedWaitHandle.Set();
        }

        private void ProcessRemoteDelegateInvocationMessage(WireMessage message)
        {
            byte[] sharedSecret =
                MessageEncryption
                    ? _sessionId.ToByteArray()
                    : null;
            
            var delegateInvocationMessage =
                Serializer
                    .Deserialize<RemoteDelegateInvocationMessage>(
                        MessageEncryptionManager.GetDecryptedMessageData(
                            message: message,
                            sharedSecret: sharedSecret));
            
            var localDelegate =
                _delegateRegistry.GetDelegateByHandlerKey(delegateInvocationMessage.HandlerKey);

            // Invoke local delegate with arguments from remote caller
            localDelegate.DynamicInvoke(delegateInvocationMessage.DelegateArguments);
        }
        
        private void ProcessRpcResultMessage(WireMessage message)
        {
            byte[] sharedSecret =
                MessageEncryption
                    ? _sessionId.ToByteArray()
                    : null;

            Guid unqiueCallKey = 
                message.UniqueCallKey == null
                    ? Guid.Empty 
                    : new Guid(message.UniqueCallKey);
            
            if (!_activeCalls.TryGetValue(unqiueCallKey, out ClientRpcContext clientRpcContext))
                throw new KeyNotFoundException("Received a result for a unknown call.");
            
            clientRpcContext.Error = message.Error;

            if (message.Error)
            {
                var remoteException =
                    Serializer.Deserialize<RemoteInvocationException>(
                        MessageEncryptionManager.GetDecryptedMessageData(
                            message: message,
                            sharedSecret: sharedSecret),
                        knownTypes: clientRpcContext.KnownTypes);

                clientRpcContext.RemoteException = remoteException;
            }
            else
            {
                var resultMessage =
                    Serializer
                        .Deserialize<MethodCallResultMessage>(
                            MessageEncryptionManager.GetDecryptedMessageData(
                                message: message,
                                sharedSecret: sharedSecret),
                            knownTypes: clientRpcContext.KnownTypes);

                clientRpcContext.ResultMessage = resultMessage;
            }

            clientRpcContext.WaitHandle.Set();
        }
        
        #endregion
        
        #region RPC
        
        /// <summary>
        /// Calls a method on a remote service synchronously.
        /// </summary>
        /// <param name="methodCallMessage">Details of the remote method to be invoked</param>
        /// <param name="oneWay">Invoke methode without waiting for or proceesing result.</param>
        /// <param name="knownTypes">Optional list of known types</param>
        /// <returns>Results of the remote method invocation</returns>
        internal ClientRpcContext InvokeRemoteMethod(MethodCallMessage methodCallMessage, bool oneWay = false, IEnumerable<Type> knownTypes = null)
        {
            byte[] sharedSecret =
                MessageEncryption
                    ? _sessionId.ToByteArray()
                    : null;

            var clientRpcContext = new ClientRpcContext();

            if (!_activeCalls.TryAdd(clientRpcContext.UniqueCallKey, clientRpcContext))
                throw new ApplicationException("Duplicate uniqe call key.");

            clientRpcContext.KnownTypes = knownTypes;
            var knownTypesList = (clientRpcContext.KnownTypes ?? Type.EmptyTypes).ToList();

            var wireMessage =
                MessageEncryptionManager.CreateWireMessage(
                    messageType: "rpc",
                    serializedMessage: Serializer.Serialize(methodCallMessage, knownTypesList),
                    sharedSecret: sharedSecret,
                    uniqueCallKey: clientRpcContext.UniqueCallKey.ToByteArray());

            byte[] rawData = Serializer.Serialize(wireMessage);

            _rawMessageTransport.LastException = null;
            
            _rawMessageTransport.SendMessage(rawData);

            if (_rawMessageTransport.LastException != null)
                throw _rawMessageTransport.LastException;

            if (!oneWay && clientRpcContext.ResultMessage == null)
            {
                if (_config.InvocationTimeout <= 0)
                    clientRpcContext.WaitHandle.WaitOne();
                else
                    clientRpcContext.WaitHandle.WaitOne(_config.InvocationTimeout * 1000);
            }

            return clientRpcContext;
        }
        
        #endregion
        
        #region Proxy management
        
        /// <summary>
        /// Creates a proxy object to provide access to a remote service.
        /// </summary>
        /// <typeparam name="T">Type of the shared interface of the remote service</typeparam>
        /// <returns>Proxy object</returns>
        public T CreateProxy<T>()
        {
            return (T) _proxyGenerator.CreateInterfaceProxyWithoutTarget(
                interfaceToProxy: typeof(T),
                interceptor: new ServiceProxy<T>(this));
        }

        /// <summary>
        /// Creates a proxy object to provide access to a remote service.
        /// </summary>
        /// <param name="serviceInterfaceType">Interface type of the remote service</param>
        /// <returns>Proxy object</returns>
        public object CreateProxy(Type serviceInterfaceType)
        {
            var serviceProxyType = typeof(ServiceProxy<>).MakeGenericType(serviceInterfaceType);
            var serviceProxy = Activator.CreateInstance(serviceProxyType, this);
            
            return _proxyGenerator.CreateInterfaceProxyWithoutTarget(
                interfaceToProxy: serviceInterfaceType,
                interceptor: (IInterceptor)serviceProxy);
        }

        /// <summary>
        /// Shuts a specified service proxy down and frees ressources.
        /// </summary>
        /// <param name="serviceProxy"></param>
        public void ShutdownProxy(object serviceProxy)
        {
            if (!ProxyUtil.IsProxy(serviceProxy))
                return;
            
            var proxyType = serviceProxy.GetType();
            
            var hiddenInterceptorsField = 
                proxyType.GetField("__interceptors", 
                    BindingFlags.Instance | BindingFlags.NonPublic);

            if (hiddenInterceptorsField == null)
                return;
            
            var interceptors =  hiddenInterceptorsField.GetValue(serviceProxy) as IInterceptor[];

            var coreRemotingInterceptor =
                (from interceptor in interceptors
                    where interceptor is IServiceProxy
                    select interceptor).FirstOrDefault();

            ((IServiceProxy) coreRemotingInterceptor)?.Shutdown();
        }

        #endregion
        
        #region IDisposable implementation
        
        /// <summary>
        /// Frees managed resources.
        /// </summary>
        public void Dispose()
        {
            Disconnect();
            
            _cancellationTokenSource.Cancel();
            _delegateRegistry.Clear();

            if (_rawMessageTransport != null)
            {
                _rawMessageTransport.ReceiveMessage -= OnMessage;
                _rawMessageTransport = null;
            }

            if (_channel != null)
            {   
                _channel.Dispose();
                _channel = null;
            }

            if (_handshakeCompletedWaitHandle != null)
            {
                _handshakeCompletedWaitHandle.Dispose();
                _handshakeCompletedWaitHandle = null;
            }

            if (_authenticationCompletedWaitHandle != null)
            {
                _authenticationCompletedWaitHandle.Dispose();
                _authenticationCompletedWaitHandle = null;
            }

            if (_goodbyeCompletedWaitHandle != null)
            {
                _goodbyeCompletedWaitHandle.Dispose();
                _goodbyeCompletedWaitHandle = null;
            }

            _keyPair?.Dispose();
        }
        
        #endregion
    }
}
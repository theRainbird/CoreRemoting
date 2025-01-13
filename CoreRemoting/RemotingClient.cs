using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Threading.Tasks;
using System.Timers;
using Castle.DynamicProxy;
using CoreRemoting.Authentication;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Tcp;
using CoreRemoting.RpcMessaging;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.Encryption;
using CoreRemoting.Serialization;
using CoreRemoting.Serialization.Bson;
using CoreRemoting.Toolbox;
using CancellationTokenSource = System.Threading.CancellationTokenSource;
using Timer = System.Timers.Timer;

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
        private readonly RsaKeyPair _keyPair;
        private readonly ClientDelegateRegistry _delegateRegistry;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly ClientConfig _config;
        private Dictionary<Guid, ClientRpcContext> _activeCalls;
        private readonly object _syncObject;
        private Guid _sessionId;
        private TaskCompletionSource<bool> _handshakeCompletedTaskSource;
        private TaskCompletionSource<bool> _authenticationCompletedTaskSource;
        private TaskCompletionSource<bool> _goodbyeCompletedTaskSource;
        private bool _isAuthenticated;
        private Timer _keepSessionAliveTimer;
        private byte[] _serverPublicKeyBlob;

        // ReSharper disable once InconsistentNaming
        private static readonly ConcurrentDictionary<string, IRemotingClient> _clientInstances = new();

        private static WeakReference<IRemotingClient> _defaultRemotingClientRef;

        /// <summary>
        /// Event: Fires after client was disconnected.
        /// </summary>
        public event Action AfterDisconnect;

        #endregion

        #region Construction

        private RemotingClient()
        {
            MethodCallMessageBuilder = new MethodCallMessageBuilder();
            MessageEncryptionManager = new MessageEncryptionManager();
            _activeCalls = null;
            _syncObject = new();
            _cancellationTokenSource = new();
            _delegateRegistry = new();
            _handshakeCompletedTaskSource = new();
            _authenticationCompletedTaskSource = new();
            _goodbyeCompletedTaskSource = new();
        }

        /// <summary>
        /// Creates a new instance of the RemotingClient class.
        /// </summary>
        /// <param name="config">Configuration settings</param>
        public RemotingClient(ClientConfig config) : this()
        {
            if (config == null)
                throw new ArgumentException("No config provided and no default configuration found.");

            Serializer = config.Serializer ?? new BsonSerializerAdapter();
            MessageEncryption = config.MessageEncryption;

            _config = config;

            if (MessageEncryption)
                _keyPair = new RsaKeyPair(config.KeySize);

            _channel = config.Channel ?? new TcpClientChannel();

            _channel.Init(this);
            _channel.Disconnected += OnDisconnected;
            _rawMessageTransport = _channel.RawMessageTransport;
            _rawMessageTransport.ReceiveMessage += OnMessage;
            _rawMessageTransport.ErrorOccured += (s, exception) =>
            {
                if (exception != null)
                    throw exception;

                throw new NetworkException(s);
            };

            _clientInstances.AddOrUpdate(
                key: config.UniqueClientInstanceName,
                addValueFactory: _ => this,
                updateValueFactory: (_, oldClient) =>
                {
                    oldClient?.Dispose();
                    return this;
                });

            if (!config.IsDefault)
                return;

            RemotingClient.DefaultRemotingClient ??= this;
        }

        private void OnDisconnected()
        {
            var activeCalls = _activeCalls;
            _activeCalls = null;

            _goodbyeCompletedTaskSource.TrySetResult(true);

            if (activeCalls == null)
                return;

            foreach (var activeCall in activeCalls)
            {
                activeCall.Value.Error = true;
                activeCall.Value.RemoteException = new RemoteInvocationException("Server Disconnected");
                activeCall.Value.TaskSource.TrySetResult(null);
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the proxy generator instance.
        /// </summary>
        private static readonly ProxyGenerator ProxyGenerator = new();

        /// <summary>
        /// Gets a utility object for building remoting messages.
        /// </summary>
        internal IMethodCallMessageBuilder MethodCallMessageBuilder { get; set; }

        /// <summary>
        /// Gets a utility object to provide encryption of remoting messages.
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

        /// <summary>
        /// Gets the configuration settings used by the CoreRemoting client instance.
        /// </summary>
        public ClientConfig Config => _config;

        /// <summary>
        /// Gets the public key of this CoreRemoting client instance.
        /// </summary>
        public byte[] PublicKey => _keyPair?.PublicKey;

        /// <summary>
        /// Gets whether the connection to the server is established or not.
        /// </summary>
        public bool IsConnected => _channel?.IsConnected ?? false;

        /// <summary>
        /// Gets whether this CoreRemoting client instance has a session or not.
        /// </summary>
        public bool HasSession
        {
            get
            {
                lock(_syncObject)
                {
                    return _sessionId != Guid.Empty;
                }
            }
        }


        /// <summary>
        /// Gets the authenticated identity. May be null if authentication failed or if authentication is not configured.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
        public RemotingIdentity Identity { get; private set; }

        #endregion

        #region Connection management

        /// <summary>
        /// Connects this CoreRemoting client instance to the configured CoreRemoting server.
        /// </summary>
        /// <exception cref="RemotingException">Thrown, if no channel is configured.</exception>
        /// <exception cref="NetworkException">Thrown, if handshake with server failed.</exception>
        public void Connect() =>
            ConnectAsync().JustWait();

        /// <summary>
        /// Connects this CoreRemoting client instance to the configured CoreRemoting server.
        /// </summary>
        /// <exception cref="RemotingException">Thrown, if no channel is configured.</exception>
        /// <exception cref="NetworkException">Thrown, if handshake with server failed.</exception>
        public async Task ConnectAsync()
        {
            if (_channel == null)
                throw new RemotingException("No client channel configured.");

            _goodbyeCompletedTaskSource = new();
            lock(_syncObject)
                _activeCalls = new Dictionary<Guid, ClientRpcContext>();

            await _channel.ConnectAsync()
                .ConfigureAwait(false);

            if (_channel.RawMessageTransport.LastException != null)
                throw _channel.RawMessageTransport.LastException;

            await _handshakeCompletedTaskSource.Task.Timeout(
                _config.ConnectionTimeout, () =>
                    throw new NetworkException("Handshake with server failed."))
                .ConfigureAwait(false);

            await AuthenticateAsync()
                .ConfigureAwait(false);

            StartKeepSessionAliveTimer();
        }

        /// <summary>
        /// Disconnects from the server. The server is actively notified about disconnection.
        /// </summary>
        /// <param name="quiet">When set to true, no goodbye message is sent to the server</param>
        public void Disconnect(bool quiet = false) =>
            DisconnectAsync(quiet).JustWait();

        /// <summary>
        /// Disconnects from the server. The server is actively notified about disconnection.
        /// </summary>
        /// <param name="quiet">When set to true, no goodbye message is sent to the server</param>
        public async Task DisconnectAsync(bool quiet = false)
        {
            if (_channel == null)
                return;

            Guid sessionId;
            lock (_syncObject)
            {
                if (_sessionId == Guid.Empty)
                    return;
                sessionId = _sessionId;
                _sessionId = Guid.Empty;
            }

            if (_keepSessionAliveTimer != null)
            {
                _keepSessionAliveTimer.Stop();
                _keepSessionAliveTimer.Dispose();
                _keepSessionAliveTimer = null;
            }

            byte[] sharedSecret =
                MessageEncryption
                    ? sessionId.ToByteArray()
                    : null;

            if (!quiet)
            {
                var goodbyeMessage =
                    new GoodbyeMessage
                    {
                        SessionId = sessionId
                    };

                var wireMessage =
                    MessageEncryptionManager.CreateWireMessage(
                        messageType: "goodbye",
                        serializer: Serializer,
                        serializedMessage: Serializer.Serialize(goodbyeMessage),
                        keyPair: _keyPair,
                        sharedSecret: sharedSecret);

                byte[] rawData = Serializer.Serialize(wireMessage);

                //_goodbyeCompletedWaitHandle.Reset();

                if (await _channel.RawMessageTransport.SendMessageAsync(rawData).ConfigureAwait(false))
                    await _goodbyeCompletedTaskSource.Task.Timeout(10).ConfigureAwait(false);
            }

            // lock (_syncObject) // TODO: why we are locking here?
            {
                var channel = _channel;
                if (channel != null)
                    await channel.DisconnectAsync().ConfigureAwait(false);
            }

            OnDisconnected();
            _handshakeCompletedTaskSource = new();
            _authenticationCompletedTaskSource = new();
            Identity = null;

            AfterDisconnect?.Invoke();
        }

        /// <summary>
        /// Starts the keep session alive timer.
        /// </summary>
        private void StartKeepSessionAliveTimer()
        {
            if (_config.KeepSessionAliveInterval <= 0)
                return;

            _keepSessionAliveTimer =
                new Timer(Convert.ToDouble(_config.KeepSessionAliveInterval * 1000));

            _keepSessionAliveTimer.Elapsed += KeepSessionAliveTimerOnElapsed;
            _keepSessionAliveTimer.Start();
        }

        /// <summary>
        /// Event procedure: Called when the keep session alive timer elapses.
        /// </summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event arguments</param>
        private void KeepSessionAliveTimerOnElapsed(object sender, ElapsedEventArgs e)
        {
            if (_keepSessionAliveTimer == null)
                return;

            if (!_keepSessionAliveTimer.Enabled)
                return;

            if (_rawMessageTransport == null)
                return;

            if (!HasSession)
            {
                OnDisconnected();
                return;
            }

            // Send empty message to keep session alive
            _rawMessageTransport.SendMessageAsync([]).JustWait();
        }

        private byte[] SharedSecret()
        {
            if (MessageEncryption)
            {
                lock (_syncObject)
                {
                    return _sessionId.ToByteArray();
                }
            }
            else
            {
                return null;
            }
        }

        #endregion

        #region Authentication

        /// <summary>
        /// Authenticates this CoreRemoting client instance with the specified credentials.
        /// </summary>
        /// <exception cref="SecurityException">Thrown, if authentication failed or timed out</exception>
        private async Task AuthenticateAsync()
        {
            if (_config.Credentials == null || (_config.Credentials!=null && _config.Credentials.Length == 0))
                return;

            if (_authenticationCompletedTaskSource.Task.IsCompleted)
                return;

            var sharedSecret = SharedSecret();

            var authRequestMessage =
                new AuthenticationRequestMessage
                {
                    Credentials = _config.Credentials
                };

            var wireMessage =
                MessageEncryptionManager.CreateWireMessage(
                    messageType: "auth",
                    serializer: Serializer,
                    serializedMessage: Serializer.Serialize(authRequestMessage),
                    keyPair: _keyPair,
                    sharedSecret: sharedSecret);

            var rawData = Serializer.Serialize(wireMessage);

            _rawMessageTransport.LastException = null;

            await _rawMessageTransport.SendMessageAsync(rawData)
                .ConfigureAwait(false);

            if (_rawMessageTransport.LastException != null)
                throw _rawMessageTransport.LastException;

            await _authenticationCompletedTaskSource.Task.Timeout(
                _config.AuthenticationTimeout, () =>
                    throw new SecurityException("Authentication timeout."))
                        .ConfigureAwait(false);

            if (!_isAuthenticated)
                throw new SecurityException("Authentication failed. Please check credentials.");
        }

        #endregion

        #region Handling received messages

        /// <summary>
        /// Called when a message is received from server.
        /// </summary>
        /// <param name="rawMessage">Raw message data</param>
        private void OnMessage(byte[] rawMessage) => Task.Run(() =>
        {
            var message = TryDeserialize(rawMessage);

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
                    _goodbyeCompletedTaskSource.TrySetResult(true);
                    break;
                case "session_closed":
                    Disconnect(quiet: true);
                    break;
                default:
                    // TODO: how do we handle invalid wire messages received by the client?
                    // A wire message could have been tampered with and couldn't be deserialized
                    break;
            }
        }).ConfigureAwait(false);

        private WireMessage TryDeserialize(byte[] rawMessage)
        {
            try
            {
                return Serializer.Deserialize<WireMessage>(rawMessage);
            }
            catch // TODO: dispatch message deserialization exception?
            {
                return new WireMessage
                {
                    Data = rawMessage,
                    Error = true,
                    Iv = [],
                    MessageType = "invalid",
                    UniqueCallKey = [],
                };
            }
        }

        /// <summary>
        /// Processes a complete handshake message from server.
        /// </summary>
        /// <param name="message">Deserialized WireMessage that contains a plain or encrypted Session ID</param>
        private void ProcessCompleteHandshakeMessage(WireMessage message)
        {
            if (MessageEncryption)
            {
                var signedMessageData =
                    Serializer.Deserialize<SignedMessageData>(message.Data);

                var encryptedSecret =
                    Serializer.Deserialize<EncryptedSecret>(signedMessageData.MessageRawData);

                _serverPublicKeyBlob = encryptedSecret.SendersPublicKeyBlob;

                if (!RsaSignature.VerifySignature(
                    keySize: _keyPair?.KeySize ?? 0,
                    sendersPublicKeyBlob: _serverPublicKeyBlob,
                    rawData: signedMessageData.MessageRawData,
                    signature: signedMessageData.Signature))
                    throw new SecurityException("Verification of message signature failed.");

                lock (_syncObject)
                {
                    _sessionId =
                        new Guid(
                            RsaKeyExchange.DecryptSecret(
                                keySize: _config.KeySize,
                                // ReSharper disable once PossibleNullReferenceException
                                receiversPrivateKeyBlob: _keyPair.PrivateKey,
                                encryptedSecret: encryptedSecret));
                }
            }
            else
            {
                lock (_syncObject)
                    _sessionId = new Guid(message.Data);
            }

            _handshakeCompletedTaskSource.TrySetResult(true);
        }

        /// <summary>
        /// Processes a authentication response message from server.
        /// </summary>
        /// <param name="message">Deserialized WireMessage that contains a AuthenticationResponseMessage</param>
        private void ProcessAuthenticationResponseMessage(WireMessage message)
        {
            var sharedSecret = SharedSecret();

            var authResponseMessage =
                Serializer
                    .Deserialize<AuthenticationResponseMessage>(
                        MessageEncryptionManager.GetDecryptedMessageData(
                            message: message,
                            serializer: Serializer,
                            sharedSecret: sharedSecret,
                            sendersPublicKeyBlob: _serverPublicKeyBlob,
                            sendersPublicKeySize: _keyPair?.KeySize ?? 0));

            _isAuthenticated = authResponseMessage.IsAuthenticated;

            Identity = _isAuthenticated ? authResponseMessage.AuthenticatedIdentity : null;

            _authenticationCompletedTaskSource.TrySetResult(true);
        }

        /// <summary>
        /// Processes a remote delegate invocation message from server.
        /// </summary>
        /// <param name="message">Deserialized WireMessage that contains a RemoteDelegateInvocationMessage</param>
        private void ProcessRemoteDelegateInvocationMessage(WireMessage message)
        {
            var sharedSecret = SharedSecret();

            var delegateInvocationMessage =
                Serializer
                    .Deserialize<RemoteDelegateInvocationMessage>(
                        MessageEncryptionManager.GetDecryptedMessageData(
                            message: message,
                            serializer: Serializer,
                            sharedSecret: sharedSecret,
                            sendersPublicKeyBlob: _serverPublicKeyBlob,
                            sendersPublicKeySize: _keyPair?.KeySize ?? 0));

            var localDelegate =
                _delegateRegistry.GetDelegateByHandlerKey(delegateInvocationMessage.HandlerKey);

            // Invoke local delegate with arguments from remote caller
            localDelegate.DynamicInvoke(delegateInvocationMessage.DelegateArguments);
        }

        /// <summary>
        /// Processes a RPC result message from server.
        /// </summary>
        /// <param name="message">Deserialized WireMessage that contains a MethodCallResultMessage or a RemoteInvocationException</param>
        /// <exception cref="KeyNotFoundException">Thrown, when the received result is of a unknown call</exception>
        private void ProcessRpcResultMessage(WireMessage message)
        {
            var sharedSecret = SharedSecret();

            Guid unqiueCallKey =
                message.UniqueCallKey == null
                    ? Guid.Empty
                    : new Guid(message.UniqueCallKey);

            ClientRpcContext clientRpcContext;

            lock (_syncObject)
            {
                if (_activeCalls == null)
                    return;

                if (!_activeCalls.ContainsKey(unqiueCallKey))
                    throw new KeyNotFoundException("Received a result for a unknown call.");

                clientRpcContext = _activeCalls[unqiueCallKey];

                _activeCalls.Remove(unqiueCallKey);
            }

            clientRpcContext.Error = message.Error;

            if (message.Error)
            {
                try
                {
                    var remoteException =
                        Serializer.Deserialize<Exception>(
                            MessageEncryptionManager.GetDecryptedMessageData(
                                message: message,
                                serializer: Serializer,
                                sharedSecret: sharedSecret,
                                sendersPublicKeyBlob: _serverPublicKeyBlob,
                                sendersPublicKeySize: _keyPair?.KeySize ?? 0));

                    clientRpcContext.RemoteException = remoteException;
                }
                catch (Exception deserializationException)
                {
                    var remoteException = new RemoteInvocationException(
                        "Remote exception couldn't be deserialized",
                            deserializationException);

                    clientRpcContext.RemoteException = remoteException;
                }
            }
            else
            {
                try
                {
                    var rawMessage =
                        MessageEncryptionManager.GetDecryptedMessageData(
                            message: message,
                            serializer: Serializer,
                            sharedSecret: sharedSecret,
                            sendersPublicKeyBlob: _serverPublicKeyBlob,
                            sendersPublicKeySize: _keyPair?.KeySize ?? 0);

                    var resultMessage =
                        Serializer
                            .Deserialize<MethodCallResultMessage>(rawMessage);

                    clientRpcContext.ResultMessage = resultMessage;
                }
                catch (Exception e)
                {
                    clientRpcContext.Error = true;

                    clientRpcContext.RemoteException =
                        new RemoteInvocationException(
                            message: e.Message,
                            innerEx: e.ToSerializable());
                }
            }

            clientRpcContext.TaskSource.TrySetResult(null);
        }

        #endregion

        #region RPC

        /// <summary>
        /// Calls a method on a remote service.
        /// </summary>
        /// <param name="methodCallMessage">Details of the remote method to be invoked</param>
        /// <param name="oneWay">Invoke method without waiting for or processing result.</param>
        /// <returns>Results of the remote method invocation</returns>
        internal async Task<ClientRpcContext> InvokeRemoteMethod(MethodCallMessage methodCallMessage, bool oneWay = false)
        {
            var sharedSecret = SharedSecret();

            lock (_syncObject)
            {
                if (_activeCalls == null)
                    throw new RemoteInvocationException("ServerDisconnected");
            }

            var clientRpcContext = new ClientRpcContext();

            lock (_syncObject)
            {
                if (_activeCalls.ContainsKey(clientRpcContext.UniqueCallKey))
                {
                    clientRpcContext.Dispose();
                    throw new ApplicationException("Duplicate unique call key.");
                }
                else
                {
                    _activeCalls.Add(clientRpcContext.UniqueCallKey, clientRpcContext);
                }
            }

            var wireMessage =
                MessageEncryptionManager.CreateWireMessage(
                    messageType: "rpc",
                    serializer: Serializer,
                    serializedMessage: Serializer.Serialize(methodCallMessage),
                    sharedSecret: sharedSecret,
                    keyPair: _keyPair,
                    uniqueCallKey: clientRpcContext.UniqueCallKey.ToByteArray());

            var rawData = Serializer.Serialize(wireMessage);

            _rawMessageTransport.LastException = null;

            await _rawMessageTransport.SendMessageAsync(rawData)
                .ConfigureAwait(false);

            if (_rawMessageTransport.LastException != null)
            {
                clientRpcContext.Dispose();
                throw _rawMessageTransport.LastException;
            }

            if (oneWay || clientRpcContext.ResultMessage != null)
                return clientRpcContext;

            await clientRpcContext.Task.Timeout(
                _config.InvocationTimeout,
                $"Invocation timeout ({_config.InvocationTimeout}) exceeded.")
                .ConfigureAwait(false);

            return clientRpcContext;
        }

        #endregion

        #region Proxy management

        /// <summary>
        /// Creates a proxy object to provide access to a remote service.
        /// </summary>
        /// <typeparam name="T">Type of the shared interface of the remote service</typeparam>
        /// <param name="serviceName">Unique name of the remote service</param>
        /// <returns>Proxy object</returns>
        public T CreateProxy<T>(string serviceName = "")
        {
            return (T) ProxyGenerator.CreateInterfaceProxyWithoutTarget(
                interfaceToProxy: typeof(T),
                interceptor: new ServiceProxy<T>(
                    client: this,
                    serviceName: serviceName));
        }

        /// <summary>
        /// Creates a proxy object to provide access to a remote service.
        /// </summary>
        /// <param name="serviceInterfaceType">Interface type of the remote service</param>
        /// <param name="serviceName">Unique name of the remote service</param>
        /// <returns>Proxy object</returns>
        public object CreateProxy(Type serviceInterfaceType, string serviceName = "")
        {
            var serviceProxyType = typeof(ServiceProxy<>).MakeGenericType(serviceInterfaceType);
            var serviceProxy = Activator.CreateInstance(serviceProxyType, this, serviceName);

            return ProxyGenerator.CreateInterfaceProxyWithoutTarget(
                interfaceToProxy: serviceInterfaceType,
                interceptor: (IInterceptor)serviceProxy);
        }

        /// <summary>
        /// Creates a proxy object to provide access to a remote service.
        /// </summary>
        /// <param name="serviceReference">Reference to remote service registration (This is not an object reference!)</param>
        /// <returns>Proxy object</returns>
        public object CreateProxy(ServiceReference serviceReference)
        {
            var serviceInterfaceType = Type.GetType(serviceReference.ServiceInterfaceTypeName);
            return CreateProxy(serviceInterfaceType, serviceReference.ServiceName);
        }

        /// <summary>
        /// Shuts a specified service proxy down and frees resources.
        /// </summary>
        /// <param name="serviceProxy">Proxy object that should be shut down</param>
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
            if (RemotingClient.DefaultRemotingClient == this)
                RemotingClient.DefaultRemotingClient = null;

            _clientInstances.TryRemove(_config.UniqueClientInstanceName, out _);

            Disconnect();

            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _delegateRegistry.Clear();

            if (_rawMessageTransport != null)
            {
                _rawMessageTransport.ReceiveMessage -= OnMessage;
                _rawMessageTransport = null;
            }

            lock (_syncObject)
            {
                if (_channel != null)
                {
                    _channel.Dispose();
                    _channel = null;
                }
            }

            _keyPair?.Dispose();
        }

        #endregion

        #region Managing client instances

        /// <summary>
        /// Gets a list of active client instances.
        /// </summary>
        public static IEnumerable<IRemotingClient> ActiveClientInstances => _clientInstances.Values;

        /// <summary>
        /// Gets a active client instance by its unqiue instance name.
        /// </summary>
        /// <param name="uniqueClientInstanceName">Unique client instance name</param>
        /// <returns>Active CoreRemoting client</returns>
        public static IRemotingClient GetActiveClientInstance(string uniqueClientInstanceName)
        {
            _clientInstances.TryGetValue(uniqueClientInstanceName, out var client);
            return client;
        }

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

        #endregion
    }
}

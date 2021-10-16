using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using CoreRemoting.Authentication;
using CoreRemoting.Channels;
using CoreRemoting.RpcMessaging;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.Encryption;

namespace CoreRemoting
{
    /// <summary>
    /// Implements a CoreRemoting session, which controls the CoreRemoting protocol on application layer at server side.
    /// This is doing the RPC magic of CoreRemoting at server side.
    /// </summary>
    public class RemotingSession : IDisposable
    {
        #region Fields

        private readonly IRemotingServer _server;
        private IRawMessageTransport _rawMessageTransport;
        private readonly RsaKeyPair _keyPair;
        private readonly Guid _sessionId;
        private readonly byte[] _clientPublicKeyBlob;
        private readonly RemoteDelegateInvocationEventAggregator _remoteDelegateInvocationEventAggregator;
        private IDelegateProxyFactory _delegateProxyFactory;
        private ConcurrentDictionary<Guid, IDelegateProxy> _delegateProxyCache;
        private bool _isAuthenticated;
        private DateTime _lastActivityTimestamp;

        /// <summary>
        /// Event: Fired before the session is disposed to do some clean up.
        /// </summary>
        public event Action BeforeDispose;
        
        #endregion

        #region Construction

        /// <summary>
        /// Creates a new instance of the RemotingSession class.
        /// </summary>
        /// <param name="keySize">Key size of the RSA keys for asymmetric encryption</param>
        /// <param name="clientPublicKey">Public key of this session's client</param>
        /// <param name="server">Server instance, that hosts this session</param>
        /// <param name="rawMessageTransport">Component, that does the raw message transport (send and receive)</param>
        internal RemotingSession(int keySize, byte[] clientPublicKey, IRemotingServer server,
            IRawMessageTransport rawMessageTransport)
        {
            _sessionId = Guid.NewGuid();
            _lastActivityTimestamp = DateTime.Now;
            _isAuthenticated = false;
            _keyPair = new RsaKeyPair(keySize);
            CreatedOn = DateTime.Now;
            _remoteDelegateInvocationEventAggregator = new RemoteDelegateInvocationEventAggregator();
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _delegateProxyFactory = _server.ServiceRegistry.GetService<IDelegateProxyFactory>();
            _delegateProxyCache = new ConcurrentDictionary<Guid, IDelegateProxy>();
            _rawMessageTransport = rawMessageTransport ?? throw new ArgumentNullException(nameof(rawMessageTransport));
            _clientPublicKeyBlob = clientPublicKey;
            
            _rawMessageTransport.ReceiveMessage += OnReceiveMessage;
            _rawMessageTransport.ErrorOccured += OnErrorOccured;

            MessageEncryption = clientPublicKey != null;

            WireMessage completeHandshakeMessage;

            if (MessageEncryption)
            {
                var encryptedSessionId =
                    RsaKeyExchange.EncryptSecret(
                        keySize: _server.SessionRepository.KeySize,
                        receiversPublicKeyBlob: clientPublicKey,
                        secretToEncrypt: _sessionId.ToByteArray(),
                        sendersPublicKeyBlob: _keyPair.PublicKey);

                var rawContent = _server.Serializer.Serialize(encryptedSessionId);

                var signedMessageData =
                    new SignedMessageData()
                    {
                        MessageRawData = rawContent,
                        Signature =
                            RsaSignature.CreateSignature(
                                keySize: keySize,
                                sendersPrivateKeyBlob: _keyPair.PrivateKey,
                                rawData: rawContent)
                    };

                var rawData = _server.Serializer.Serialize(typeof(SignedMessageData), signedMessageData); 

                completeHandshakeMessage =
                    new WireMessage
                    {
                        MessageType = "complete_handshake",
                        Data = rawData
                    };
            }
            else
            {
                completeHandshakeMessage =
                    new WireMessage
                    {
                        MessageType = "complete_handshake",
                        Data = _sessionId.ToByteArray()
                    };
            }

            _remoteDelegateInvocationEventAggregator.RemoteDelegateInvocationNeeded +=
                (delegateType, uniqueCallKey, handlerKey, arguments) =>
                {
                    var sharedSecret =
                        MessageEncryption
                            ? _sessionId.ToByteArray()
                            : null;

                    var remoteDelegateInvocationMessage =
                        new RemoteDelegateInvocationMessage
                        {
                            UniqueCallKey = uniqueCallKey,
                            HandlerKey = handlerKey,
                            DelegateArguments = arguments
                        };
                    
                    var remoteDelegateInvocationWebsocketMessage =
                        _server.MessageEncryptionManager
                            .CreateWireMessage(
                                serializedMessage: _server.Serializer.Serialize(remoteDelegateInvocationMessage),
                                serializer: _server.Serializer,
                                sharedSecret: sharedSecret,
                                keyPair: _keyPair,
                                messageType: "invoke");

                    // Invoke remote delegate on client
                    _rawMessageTransport.SendMessage(
                        _server.Serializer.Serialize(remoteDelegateInvocationWebsocketMessage));

                    return null;
                };

            _rawMessageTransport.SendMessage(_server.Serializer.Serialize(completeHandshakeMessage));
        }

        /// <summary>
        /// Event procedure: Called if the ErrorOccured event is fired on the raw message transport component.
        /// </summary>
        /// <param name="errorMessage">Error message</param>
        /// <param name="ex">Optional exception from the transport infrastructure</param>
        private void OnErrorOccured(string errorMessage, Exception ex)
        {
            var exception = new RemotingException(errorMessage, innerEx: ex); 
            
            ((RemotingServer)_server).OnError(exception);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the timestamp of the last activity of this session.
        /// </summary>
        public DateTime LastActivityTimestamp => _lastActivityTimestamp;

        /// <summary>
        /// Gets this session's unique session ID.
        /// </summary>
        public Guid SessionId => _sessionId;

        /// <summary>
        /// Gets whether message encryption is enabled for this session.
        /// </summary>
        [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
        public bool MessageEncryption { get; }

        /// <summary>
        /// Gets the timestamp when this session was created.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
        [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
        public DateTime CreatedOn { get; }

        /// <summary>
        /// Gets whether authentication was successful.
        /// </summary>
        public bool IsAuthenticated => _isAuthenticated;

        /// <summary>
        /// Gets the server side RSA key pair of this session.
        /// </summary>
        internal RsaKeyPair KeyPair => _keyPair;

        /// <summary>
        /// Gets the remote delegate invocation event aggregator.
        /// </summary>
        [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
        internal RemoteDelegateInvocationEventAggregator RemoteDelegateInvocation =>
            _remoteDelegateInvocationEventAggregator;

        /// <summary>
        /// Gets component that does the raw message transport (send and receive).
        /// </summary>
        internal IRawMessageTransport Messaging => _rawMessageTransport;

        /// <summary>
        /// Gets the authenticated identity of this session.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
        [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
        public RemotingIdentity Identity { get; private set; }

        #endregion

        #region Handling received messages

        /// <summary>
        /// Event procedure: Called when the ReceiveMessage event is fired on the raw message transport component.
        /// </summary>
        /// <param name="rawMessage">Raw message data that has been received</param>
        private void OnReceiveMessage(byte[] rawMessage)
        {
            _lastActivityTimestamp = DateTime.Now;
            
            if (rawMessage == null)
                return;
            
            if (rawMessage.Length == 0)
                return;
            
            var message = _server.Serializer.Deserialize<WireMessage>(rawMessage);

            switch (message.MessageType.ToLower())
            {
                case "auth":
                    ProcessAuthenticationRequestMessage(message);
                    break;
                case "rpc":
                    ProcessRpcMessage(message);
                    break;
                case "goodbye":
                    ProcessGoodbyeMessage(message);
                    break;
                default:
                    OnErrorOccured("Invalid message type " + message.MessageType + ".", ex: null);
                    break;
            }
        }

        /// <summary>
        /// Processes a wire message that contains a goodbye message, which is sent from a client to close the session. 
        /// </summary>
        /// <param name="request">Wire message from client</param>
        private void ProcessGoodbyeMessage(WireMessage request)
        {
            var sharedSecret =
                MessageEncryption
                    ? SessionId.ToByteArray()
                    : null;

            var goodbyeMessage =
                _server.Serializer
                    .Deserialize<GoodbyeMessage>(
                        _server.MessageEncryptionManager.GetDecryptedMessageData(
                            message: request,
                            serializer: _server.Serializer,
                            sharedSecret: sharedSecret,
                            sendersPublicKeyBlob: _clientPublicKeyBlob,
                            sendersPublicKeySize: _keyPair?.KeySize ?? 0));
            
            if (goodbyeMessage.SessionId != _sessionId)
                return;

            var resultMessage =
                _server.MessageEncryptionManager.CreateWireMessage(
                    messageType: request.MessageType,
                    serializedMessage: new byte[0],
                    serializer: _server.Serializer,
                    keyPair: _keyPair,
                    sharedSecret: sharedSecret,
                    uniqueCallKey: request.UniqueCallKey);
            
            _rawMessageTransport.SendMessage(_server.Serializer.Serialize(resultMessage));
            
            _server.SessionRepository.RemoveSession(_sessionId);
        }

        /// <summary>
        /// Processes a wire message that contains a authentication request message, which is sent from a client to request authentication of a set of credentials. 
        /// </summary>
        /// <param name="request">Wire message from client</param>
        private void ProcessAuthenticationRequestMessage(WireMessage request)
        {
            if (_isAuthenticated)
                return;

            Identity = null;

            var sharedSecret =
                MessageEncryption
                    ? SessionId.ToByteArray()
                    : null;

            var authRequestMessage =
                _server.Serializer
                    .Deserialize<AuthenticationRequestMessage>(
                        _server.MessageEncryptionManager.GetDecryptedMessageData(
                            message: request,
                            serializer: _server.Serializer,
                            sharedSecret: sharedSecret,
                            sendersPublicKeyBlob: _clientPublicKeyBlob,
                            sendersPublicKeySize: _keyPair?.KeySize ?? 0));

            _isAuthenticated = _server.Authenticate(authRequestMessage.Credentials, out var authenticatedIdentity);

            if (_isAuthenticated)
                Identity = authenticatedIdentity;

            var authResponseMessage =
                new AuthenticationResponseMessage
                {
                    IsAuthenticated = _isAuthenticated,
                    AuthenticatedIdentity = authenticatedIdentity
                };

            var serializedAuthResponse = _server.Serializer.Serialize(authResponseMessage);

            var wireMessage =
                _server.MessageEncryptionManager.CreateWireMessage(
                    serializedMessage: serializedAuthResponse,
                    serializer: _server.Serializer,
                    sharedSecret: sharedSecret,
                    keyPair: _keyPair,
                    messageType: "auth_response");

            _rawMessageTransport.SendMessage(
                _server.Serializer.Serialize(wireMessage));
        }

        /// <summary>
        /// Calls a method on a server side service.
        /// </summary>
        /// <param name="request">RPC message from client</param>
        /// <returns>Task which provides the serialized response message containing the method result asynchronously</returns>
        /// <exception cref="MissingMethodException">Thrown if specified method in request doesn't exist</exception>
        private void ProcessRpcMessage(WireMessage request)
        {
            var sharedSecret =
                MessageEncryption
                    ? SessionId.ToByteArray()
                    : null;
            
            var callMessage =
                _server.Serializer
                    .Deserialize<MethodCallMessage>(
                        _server.MessageEncryptionManager.GetDecryptedMessageData(
                            message: request,
                            serializer: _server.Serializer,
                            sharedSecret: sharedSecret,
                            sendersPublicKeyBlob: _clientPublicKeyBlob,
                            sendersPublicKeySize: _keyPair?.KeySize ?? 0));

            ServerRpcContext serverRpcContext = 
                new ServerRpcContext
                {
                    UniqueCallKey = 
                        request.UniqueCallKey == null
                            ? Guid.Empty
                            : new Guid(request.UniqueCallKey),
                    ServiceInstance = null,
                    MethodCallMessage = callMessage,
                    Session = this
                };
            
            bool oneWay = false;
            byte[] serializedResult;
            
            try
            {
                ((RemotingServer) _server).OnBeforeCall(serverRpcContext);

                var service = _server.ServiceRegistry.GetService(callMessage.ServiceName);
                var serviceInterfaceType =
                    _server.ServiceRegistry.GetServiceInterfaceType(callMessage.ServiceName);

                CallContext.RestoreFromSnapshot(callMessage.CallContextSnapshot);

                serverRpcContext.ServiceInstance = service;
                
                callMessage.UnwrapParametersFromDeserializedMethodCallMessage(
                    out var parameterValues, 
                    out var parameterTypes);

                parameterValues = MapDelegateArguments(parameterValues);

                MethodInfo method;
                
                if (callMessage.GenericArgumentTypeNames != null && callMessage.GenericArgumentTypeNames.Length > 0)
                {
                    var methods = serviceInterfaceType.GetMethods();
                    method = 
                        methods
                            .SingleOrDefault(m => m.IsGenericMethod && m.Name.Equals(callMessage.MethodName, StringComparison.Ordinal));

                    if (method != null)
                    {
                        Type[] genericArguments =
                            callMessage.GenericArgumentTypeNames
                                .Select(typeName => Type.GetType(typeName))
                                .ToArray();
                        
                        method = method.MakeGenericMethod(genericArguments);
                    }
                }
                else
                {
                    method =
                        serviceInterfaceType.GetMethod(
                            name: callMessage.MethodName,
                            types: parameterTypes);    
                }

                if (method == null)
                    throw new MissingMethodException(
                        className: callMessage.ServiceName,
                        methodName: callMessage.MethodName);

                oneWay = method.GetCustomAttribute<OneWayAttribute>() != null;
            
                if (_server.Config.AuthenticationRequired && !_isAuthenticated)
                    throw new NetworkException("Session is not authenticated.");
                
                var result = method.Invoke(service, parameterValues);

                if (!oneWay)
                {
                    serverRpcContext.MethodCallResultMessage =
                        _server
                            .MethodCallMessageBuilder
                            .BuildMethodCallResultMessage(
                                serializer: _server.Serializer,
                                uniqueCallKey: serverRpcContext.UniqueCallKey,
                                method: method,
                                args: parameterValues,
                                returnValue: result);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                
                if (serverRpcContext == null)
                    return;
                
                serverRpcContext.Exception = 
                    new RemoteInvocationException(
                        message: ex.Message,
                        innerEx: ex.GetType().IsSerializable ? ex : null);
            }
            finally
            {
                ((RemotingServer)_server).OnAfterCall(serverRpcContext);
            }

            if (oneWay)
                return;

            serializedResult = 
                serverRpcContext.Exception != null 
                    ? _server.Serializer.Serialize(serverRpcContext.Exception) 
                    : _server.Serializer.Serialize(serverRpcContext.MethodCallResultMessage);

            var methodResultMessage =
                _server.MessageEncryptionManager.CreateWireMessage(
                    serializedMessage: serializedResult,
                    serializer: _server.Serializer,
                    error: serverRpcContext.Exception != null,
                    sharedSecret: sharedSecret,
                    keyPair: _keyPair,
                    messageType: "rpc_result",
                    uniqueCallKey: serverRpcContext.UniqueCallKey.ToByteArray());

            _rawMessageTransport.SendMessage(
                _server.Serializer.Serialize(methodResultMessage));
        }

        /// <summary>
        /// Maps delegate arguments into delegate proxies.
        /// </summary>
        /// <param name="parameterValues">Array of parameter values</param>
        /// <returns>Array of parameter values where delegate values are mapped into delegate proxies</returns>
        /// <exception cref="ArgumentNullException">Thrown if no session is provided</exception>
        private object[] MapDelegateArguments(object[] parameterValues)
        {
            var arguments =
                parameterValues?.Select(argument =>
                {
                    if (argument is RemoteDelegateInfo remoteDelegateInfo)
                    {
                        if (_delegateProxyCache.ContainsKey(remoteDelegateInfo.HandlerKey))
                            return _delegateProxyCache[remoteDelegateInfo.HandlerKey].ProxiedDelegate;

                        var delegateType = Type.GetType(remoteDelegateInfo.DelegateTypeName);

                        // Forge a delegate proxy and initiate remote delegate invocation, when it is invoked
                        var delegateProxy =
                            _delegateProxyFactory.Create(delegateType, delegateArgs =>
                                RemoteDelegateInvocation
                                    .InvokeRemoteDelegate(
                                        delegateType: delegateType,
                                        handlerKey: remoteDelegateInfo.HandlerKey,
                                        remoteDelegateArguments: delegateArgs));

                        _delegateProxyCache.TryAdd(remoteDelegateInfo.HandlerKey, delegateProxy);

                        return delegateProxy.ProxiedDelegate;
                    }
                    
                    return argument;
                }).ToArray();

            return arguments;
        }

        #endregion

        #region IDisposable implementation

        /// <summary>
        /// Frees managed resources.
        /// </summary>
        public void Dispose()
        {
            BeforeDispose?.Invoke();
            
            _keyPair?.Dispose();
            _delegateProxyFactory = null;
            _delegateProxyCache.Clear();
            _delegateProxyCache = null;
            _rawMessageTransport = null;
        }

        #endregion
    }
}

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Authentication;
using CoreRemoting.Channels;
using CoreRemoting.Encryption;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.RpcMessaging;
using CoreRemoting.Serialization;
using CoreRemoting.Threading;
using CoreRemoting.Toolbox;
using Serialize.Linq.Extensions;
using Serialize.Linq.Nodes;

namespace CoreRemoting;

/// <summary>
/// Implements a CoreRemoting session, which controls the CoreRemoting protocol on application layer at server side.
/// This is doing the RPC magic of CoreRemoting at server side.
/// </summary>
public sealed class RemotingSession : IAsyncDisposable
{
    #region Fields

    private readonly IRemotingServer _server;
    private IRawMessageTransport _rawMessageTransport;
    private readonly RsaKeyPair _keyPair;
    private readonly Guid _sessionId;
    private readonly byte[] _clientPublicKeyBlob;
    private readonly string _clientAddress;
    private readonly RemoteDelegateInvocationEventAggregator _remoteDelegateInvocationEventAggregator;
    private IDelegateProxyFactory _delegateProxyFactory;
    private ConcurrentDictionary<Guid, IDelegateProxy> _delegateProxyCache;
    private bool _isAuthenticated;
    private bool _isDisposing;
    private DateTime _lastActivityTimestamp;
    private readonly AsyncCountdownEvent _currentlyProcessedMessagesCounter;
    private static readonly AsyncLocal<RemotingSession> CurrentSession = new();

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
    /// <param name="clientAddress">Client's network address</param>
    /// <param name="server">Server instance, that hosts this session</param>
    /// <param name="rawMessageTransport">Component, that does the raw message transport (send and receive)</param>
    internal RemotingSession(int keySize, byte[] clientPublicKey, string clientAddress,
        IRemotingServer server, IRawMessageTransport rawMessageTransport)
    {
        _isDisposing = false;
        _currentlyProcessedMessagesCounter = new(initialCount: 1);
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
        _clientAddress = clientAddress;

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
                new SignedMessageData
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
            async (_, uniqueCallKey, handlerKey, arguments) =>
            {
                // handle graceful client disconnection
                if (_isDisposing)
                    return;

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

                try
                {
                    // Invoke remote delegate on client
                    await (_rawMessageTransport?.SendMessageAsync(
                        _server.Serializer.Serialize(remoteDelegateInvocationWebsocketMessage)) ?? Task.CompletedTask)
                            .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // handle unexpected client disconnection
                    OnErrorOccured("Failed to dispatch the remote event. " +
                        $"Session: {SessionId}, Unique call key: {uniqueCallKey}, " +
                        $"Handler key: {handlerKey}", ex);
                }
            };

        _rawMessageTransport?.SendMessageAsync(
            _server.Serializer.Serialize(completeHandshakeMessage))
                .JustWait();
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
    /// Gets this session's client network address.
    /// </summary>
    public string ClientAddress => _clientAddress;

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
    private void OnReceiveMessage(byte[] rawMessage) => Task.Run(async () =>
    {
        _lastActivityTimestamp = DateTime.Now;

        if (rawMessage == null || rawMessage.Length == 0 || _isDisposing)
            return;

        _currentlyProcessedMessagesCounter.AddCount(1);

        CurrentSession.Value = this;

        try
        {
            var message = _server.Serializer.Deserialize<WireMessage>(rawMessage);

            switch (message.MessageType.ToLower())
            {
                case "auth":
                    await ProcessAuthenticationRequestMessage(message).ConfigureAwait(false);
                    break;
                case "rpc":
                    await ProcessRpcMessage(message).ConfigureAwait(false);
                    break;
                case "goodbye":
                    await ProcessGoodbyeMessage(message).ConfigureAwait(false);
                    break;
                default:
                    OnErrorOccured("Invalid message type " + message.MessageType + ".", ex: null);
                    break;
            }
        }
        catch (Exception ex)
        {
            OnErrorOccured("Error processing message.", ex);
        }
        finally
        {
            _currentlyProcessedMessagesCounter.Signal();

            CurrentSession.Value = null;
        }
    }).ConfigureAwait(false);

    /// <summary>
    /// Processes a wire message that contains a goodbye message, which is sent from a client to close the session.
    /// </summary>
    /// <param name="request">Wire message from client</param>
    private async Task ProcessGoodbyeMessage(WireMessage request)
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
                serializedMessage: [],
                serializer: _server.Serializer,
                keyPair: _keyPair,
                sharedSecret: sharedSecret,
                uniqueCallKey: request.UniqueCallKey);

        await _rawMessageTransport.SendMessageAsync(
            _server.Serializer.Serialize(resultMessage))
                .ConfigureAwait(false);

        ((RemotingServer)_server).OnLogoff();

        await RemoveCurrentSession()
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Processes a wire message that contains a authentication request message, which is sent from a client to request authentication of a set of credentials.
    /// </summary>
    /// <param name="request">Wire message from client</param>
    private async Task ProcessAuthenticationRequestMessage(WireMessage request)
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

        await _rawMessageTransport.SendMessageAsync(
            _server.Serializer.Serialize(wireMessage))
                .ConfigureAwait(false);

        ((RemotingServer)_server).OnLogon();
    }

    /// <summary>
    /// Calls a method on a server side service.
    /// </summary>
    /// <param name="request">RPC message from client</param>
    /// <returns>Task which provides the serialized response message containing the method result asynchronously</returns>
    /// <exception cref="MissingMethodException">Thrown if specified method in request doesn't exist</exception>
    private async Task ProcessRpcMessage(WireMessage request)
    {
        var sharedSecret =
            MessageEncryption
                ? SessionId.ToByteArray()
                : null;

        var decryptedRawMessage =
            _server.MessageEncryptionManager.GetDecryptedMessageData(
                message: request,
                serializer: _server.Serializer,
                sharedSecret: sharedSecret,
                sendersPublicKeyBlob: _clientPublicKeyBlob,
                sendersPublicKeySize: _keyPair?.KeySize ?? 0);

        var callMessage =
            _server.Serializer
                .Deserialize<MethodCallMessage>(decryptedRawMessage);

        var serverRpcContext =
            new ServerRpcContext
            {
                UniqueCallKey =
                    request.UniqueCallKey == null
                        ? Guid.Empty
                        : new Guid(request.UniqueCallKey),
                AuthenticationRequired = _server.Config.AuthenticationRequired,
                ServiceInstance = null,
                MethodCallMessage = callMessage,
                MethodCallParameterValues = [],
                MethodCallParameterTypes = [],
                Session = this
            };

        using var scope = _server.ServiceRegistry.CreateScope();
        var serializedResult = Array.Empty<byte>();
        var method = default(MethodInfo);
        var oneWay = false;

        try
        {
            CallContext.RestoreFromSnapshot(callMessage.CallContextSnapshot);

            callMessage.UnwrapParametersFromDeserializedMethodCallMessage(
                out var parameterValues,
                out var parameterTypes);

            parameterValues = MapArguments(parameterValues, parameterTypes);
            serverRpcContext.MethodCallParameterValues = parameterValues;
            serverRpcContext.MethodCallParameterTypes = parameterTypes;

            ((RemotingServer)_server).OnBeginCall(serverRpcContext);

            if (serverRpcContext.AuthenticationRequired && !_isAuthenticated)
                throw new NetworkException("Session is not authenticated.");

            var registration = _server.ServiceRegistry.GetServiceRegistration(callMessage.ServiceName);
            var service = _server.ServiceRegistry.GetService(callMessage.ServiceName);
            var serviceInterfaceType = registration.InterfaceType;

            serverRpcContext.ServiceInstance = service;
            serverRpcContext.EventStub = registration.EventStub;

            method = GetMethodInfo(callMessage, serviceInterfaceType, parameterTypes);
            if (method == null)
                throw new MissingMethodException(
                    className: callMessage.ServiceName,
                    methodName: callMessage.MethodName);

            oneWay = method.GetCustomAttribute<OneWayAttribute>() != null;
        }
        catch (Exception ex)
        {
            ex = ex.SkipTargetInvocationExceptions();

            serverRpcContext.Exception =
                new RemoteInvocationException(
                    message: ex.Message,
                    innerEx: ex.ToSerializable());

            ((RemotingServer)_server).OnRejectCall(serverRpcContext);

            if (oneWay)
                throw;

            serializedResult =
                _server.Serializer.Serialize(serverRpcContext.Exception);
        }

        object result = null;

        if (serverRpcContext.Exception == null)
        {
            try
            {
                ((RemotingServer)_server).OnBeforeCall(serverRpcContext);

                if (method.IsEventAccessor(out var eventName, out var subscription))
                {
                    // event accessor is called
                    HandleEventSubscription(serverRpcContext.EventStub,
                        eventName, subscription, serverRpcContext.MethodCallParameterValues);
                    result = null;
                }
                else
                {
                    // normal method is called
                    result = method.Invoke(serverRpcContext.ServiceInstance,
                        serverRpcContext.MethodCallParameterValues);
                }

                var returnType = method.ReturnType;

                if (result != null)
                {
                    // Wait for result value if result is a Task
                    var task = await TryAwaitReturnValue(returnType, result);
                    if (task.isAwaited)
                    {
                        result = task.result;
                    }
                    else if (returnType.IsLinqExpressionType())
                    {
                        var expression = (Expression)result;
                        result = expression.ToExpressionNode();
                    }
                    else if (returnType.GetCustomAttribute<ReturnAsProxyAttribute>() != null)
                    {
                        var isRegisteredService =
                            returnType.IsInterface &&
                            _server.ServiceRegistry
                                .GetAllRegisteredTypes().Any(s =>
                                    returnType.AssemblyQualifiedName != null &&
                                    returnType.AssemblyQualifiedName.Equals(s.AssemblyQualifiedName));

                        if (!isRegisteredService)
                        {
                            throw new InvalidOperationException(
                                $"Type '{returnType.AssemblyQualifiedName}' is not a registered service.");
                        }

                        result = new ServiceReference(
                            serviceInterfaceTypeName: returnType.FullName + ", " + returnType.Assembly.GetName().Name,
                            serviceName: returnType.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                ex = ex.SkipTargetInvocationExceptions();

                serverRpcContext.Exception =
                    new RemoteInvocationException(
                        message: ex.Message,
                        innerEx: ex.ToSerializable());

                ((RemotingServer)_server).OnAfterCall(serverRpcContext);

                if (oneWay)
                    throw;

                serializedResult =
                    _server.Serializer.Serialize(serverRpcContext.Exception);
            }

            if (!oneWay)
            {
                serverRpcContext.MethodCallResultMessage =
                    _server
                        .MethodCallMessageBuilder
                        .BuildMethodCallResultMessage(
                            serializer: _server.Serializer,
                            uniqueCallKey: serverRpcContext.UniqueCallKey,
                            method: method,
                            args: serverRpcContext.MethodCallParameterValues,
                            returnValue: result);
            }

            if (serverRpcContext.Exception == null)
                ((RemotingServer)_server).OnAfterCall(serverRpcContext);

            if (oneWay)
                return;

            // don't overwrite the serialized exception
            if (ReferenceEquals(serializedResult, Array.Empty<byte>()))
            {
                try
                {
                    serializedResult =
                        _server.Serializer.Serialize(serverRpcContext.MethodCallResultMessage);
                }
                catch (Exception serializationException)
                {
                    serverRpcContext.Exception = new RemoteInvocationException(
                        message: "Failed to serialize method return value. " + serializationException.Message,
                        innerEx: serializationException.ToSerializable());

                    serializedResult = _server.Serializer.Serialize(serverRpcContext.Exception);
                }
            }
        }

        var methodResultMessage =
            _server.MessageEncryptionManager.CreateWireMessage(
                serializedMessage: serializedResult,
                serializer: _server.Serializer,
                error: serverRpcContext.Exception != null,
                sharedSecret: sharedSecret,
                keyPair: _keyPair,
                messageType: "rpc_result",
                uniqueCallKey: serverRpcContext.UniqueCallKey.ToByteArray());

        await _rawMessageTransport.SendMessageAsync(
            _server.Serializer.Serialize(methodResultMessage))
                .ConfigureAwait(false);
    }

    private async Task<(bool isAwaited, object result)> TryAwaitReturnValue(Type returnType, object result)
    {
        // Wait for result value if result is a Task or Task<T>
        if (typeof(Task).IsAssignableFrom(returnType))
        {
            var resultTask = (Task)result;
            await resultTask.ConfigureAwait(false);

            if (returnType.IsGenericType)
            {
                // extract Task.Result
                result = returnType.GetProperty("Result")?.GetValue(resultTask);
                return (true, result);
            }

            // ordinary non-generic task
            return (true, null);
        }

        // Wait for ValueTask<T>
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            // convert ValueTask<T> into Task<T>
            var asTaskMethod = returnType.GetMethod("AsTask")!;
            var resultTask = asTaskMethod.Invoke(result, null) as Task;
            await resultTask.ConfigureAwait(false);

            // extract Task.Result
            result = resultTask.GetType().GetProperty("Result")?.GetValue(resultTask);
            return (true, result);
        }

        // Wait for ValueTask
        if (returnType == typeof(ValueTask))
        {
            var resultTask = (ValueTask)result;
            await resultTask.ConfigureAwait(false);
            return (true, null);
        }

        // It's neither Task, nor ValueTask
        return (false, null);
    }

    private void HandleEventSubscription(EventStub eventStub, string eventName, bool subscription, object[] parameters)
    {
        if (parameters == null || parameters.Length != 1)
        {
            return;
        }

        var eventHandler = parameters[0] as Delegate;
        if (eventHandler == null)
        {
            return;
        }

        Action<string, Delegate> eventAccessor = subscription ?
            eventStub.AddHandler :
            eventStub.RemoveHandler;

        eventAccessor(eventName, eventHandler);
    }

    private MethodInfo GetMethodInfo(MethodCallMessage callMessage, Type serviceInterfaceType, Type[] parameterTypes)
    {
        MethodInfo method;

        if (callMessage.GenericArgumentTypeNames != null && callMessage.GenericArgumentTypeNames.Length > 0)
        {
            var methods =
                serviceInterfaceType.GetMethods().ToList();

            foreach (var inheritedInterface in serviceInterfaceType.GetInterfaces())
            {
                methods.AddRange(inheritedInterface.GetMethods());
            }

            method =
                methods.SingleOrDefault(m =>
                m.IsGenericMethod &&
                    m.Name.Equals(callMessage.MethodName, StringComparison.Ordinal));

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

            if (method == null)
            {
                foreach (var inheritedInterface in serviceInterfaceType.GetInterfaces())
                {
                    method =
                        inheritedInterface.GetMethod(
                            name: callMessage.MethodName,
                            types: parameterTypes);

                    if (method != null)
                        break;
                }
            }
        }

        return method;
    }

    /// <summary>
    /// Maps non serializable arguments into a serializable form.
    /// </summary>
    /// <param name="arguments">Array of parameter values</param>
    /// <param name="argumentTypes">Array of parameter types</param>
    /// <returns>Array of arguments (includes mapped ones)</returns>
    private object[] MapArguments(object[] arguments, Type[] argumentTypes)
    {
        object[] mappedArguments = new object[arguments.Length];

        for (int i = 0; i < arguments.Length; i++)
        {
            var argument = arguments[i];
            var type = argumentTypes[i];

            if (MapDelegateArgument(argument, out var mappedArgument))
                mappedArguments[i] = mappedArgument;
            else if (MapLinqExpressionArgument(type, argument, out mappedArgument))
                mappedArguments[i] = mappedArgument;
            else
                mappedArguments[i] = argument;
        }

        return mappedArguments;
    }

    /// <summary>
    /// Maps a delegate argument into a delegate proxy.
    /// </summary>
    /// <param name="argument">argument value</param>
    /// <param name="mappedArgument">Out: argument value where delegate value is mapped into delegate proxy</param>
    /// <returns>True if mapping applied, otherwise false</returns>
    /// <exception cref="ArgumentNullException">Thrown if no session is provided</exception>
    private bool MapDelegateArgument(object argument, out object mappedArgument)
    {
        if (!(argument is RemoteDelegateInfo remoteDelegateInfo))
        {
            mappedArgument = argument;
            return false;
        }

        if (_delegateProxyCache.TryGetValue(remoteDelegateInfo.HandlerKey, out var value))
        {
            mappedArgument = value.ProxiedDelegate;
            return true;
        }

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

        mappedArgument = delegateProxy.ProxiedDelegate;
        return true;
    }

    /// <summary>
    /// Maps a Linq expression argument into a serializable ExpressionNode object.
    /// </summary>
    /// <param name="argumentType">Type of argument to be mapped</param>
    /// <param name="argument">Argument to be wrapped</param>
    /// <param name="mappedArgument">Out: Mapped argument</param>
    /// <returns>True if mapping applied, otherwise false</returns>
    private bool MapLinqExpressionArgument(Type argumentType, object argument, out object mappedArgument)
    {
        var isLinqExpression =
            argumentType.IsGenericType &&
            argumentType.BaseType == typeof(LambdaExpression);

        if (!isLinqExpression)
        {
            mappedArgument = argument;
            return false;
        }

        var expression = ((ExpressionNode)argument).ToExpression();
        mappedArgument = expression;

        return true;
    }

    #endregion

    #region Close session

    /// <summary>
    /// Closes the session gracefully and disconnect the client.
    /// </summary>
    public void Close()
    {
        if (_isDisposing)
            return;

        // calling RemoveSession synchronously via RPC call produces a deadlock
        // in Session.Dispose because the session would wait for the current
        // RPC message processing to complete
        _ = RemoveCurrentSession();
    }

    private Task RemoveCurrentSession() => Task.Run(async () =>
    {
        // disposes the current session
        await _server?.SessionRepository.RemoveSession(_sessionId);
    });

    #endregion

    #region IAsyncDisposable and IDisposable implementations

    /// <summary>
    /// Frees managed resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposing)
            return;

        _isDisposing = true;

        _rawMessageTransport.ReceiveMessage -= OnReceiveMessage;
        _rawMessageTransport.ErrorOccured -= OnErrorOccured;

        _currentlyProcessedMessagesCounter.Signal();
        await _currentlyProcessedMessagesCounter.WaitAsync()
            .ExpireMs(_server.Config.WaitTimeForCurrentlyProcessedMessagesOnDispose)
                .ConfigureAwait(false);

        var sharedSecret =
            MessageEncryption
                ? _sessionId.ToByteArray()
                : null;

        var wireMessage =
            _server.MessageEncryptionManager.CreateWireMessage(
                serializedMessage: [],
                serializer: _server.Serializer,
                sharedSecret: sharedSecret,
                keyPair: _keyPair,
                messageType: "session_closed");

        try
        {
            await _rawMessageTransport.SendMessageAsync(
                _server.Serializer.Serialize(wireMessage))
                    .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // ignored
            // TODO: dispatch the exception
        }

        try
        {
            BeforeDispose?.Invoke();
        }
        catch (Exception)
        {
            // ignored
            // TODO: dispatch the exception
        }

        _keyPair?.Dispose();
        _delegateProxyFactory = null;
        _delegateProxyCache.Clear();
        _delegateProxyCache = null;
        _rawMessageTransport = null;
    }

    #endregion

    #region Retrieving current session

    /// <summary>
    /// Gets the current CoreRemoting server session.
    /// </summary>
    public static RemotingSession Current => CurrentSession.Value;

    #endregion
}

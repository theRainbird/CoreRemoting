using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Castle.DynamicProxy;
using CoreRemoting.Authentication;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Tcp;
using CoreRemoting.Encryption;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.RpcMessaging;
using CoreRemoting.Serialization;
using CoreRemoting.Serialization.Bson;
using CoreRemoting.Threading;
using CoreRemoting.Toolbox;
using Timer = System.Timers.Timer;

namespace CoreRemoting;

/// <summary>
/// Provides remoting functionality on client side.
/// </summary>
public sealed class RemotingClient : IRemotingClient, IAuthenticationProvider
{
    #region Fields

    private IClientChannel _channel;
    private IRawMessageTransport _rawMessageTransport;
    private readonly ISessionKeyPair _keyPair;
    private readonly ClientDelegateRegistry _delegateRegistry;
    private readonly SubscriptionTracker _subscriptionTracker;
    private readonly ClientConfig _config;
    private readonly AsyncLock _channelLock;
    private readonly AsyncLock _connectLock;
    private Dictionary<Guid, ClientRpcContext> _activeCalls;
    private readonly AsyncLock _activeCallsLock;
    private Guid _sessionId;
    private AtomicBool _authenticationRequired;
    private byte[] _sharedSecret;
    private int _sharedSecretLength;
    private readonly object _sessionLock;
    private readonly AsyncCountdownEvent _currentlyPendingMessagesCounter;
    private TaskCompletionSource<bool> _handshakeCompletedTaskSource;
    private TaskCompletionSource<AuthenticationResponseMessage> _authenticationResponseTaskSource;
    private TaskCompletionSource<bool> _authenticationCompletedTaskSource;
    private readonly AsyncManualResetEvent _goodbyeCompletedEvent;
    private AtomicBool _isAuthenticated;
    private AtomicBool _isDisposing;
    private AtomicBool _isConnected;
    private Timer _keepSessionAliveTimer;
    private byte[] _serverPublicKeyBlob;
    private AtomicBool _sessionClosedByServer;
    private readonly Lazy<IServiceInvoker<IServerSubscriptionService>> _subscriptionInvoker;
    private readonly ConcurrentDictionary<Guid, Task> _pendingUnsubscribes = new();
    private readonly SubscriptionReplayRegistry _subscriptionReplay = new();
    private AtomicBool _manualDisconnect;
    private AtomicBool _pingPending;
    private Guid _rememberedSessionId;
    private int _missedPongs;
    private int _reconnectLoopActive;
    private bool _hasConnectedBefore;
    private CancellationTokenSource _reconnectCts = new();

    // ReSharper disable once InconsistentNaming
    private static readonly ConcurrentDictionary<string, IRemotingClient> _clientInstances = new();

    private static WeakReference<IRemotingClient> _defaultRemotingClientRef;

    /// <summary>
    /// Event: Fires after client was disconnected.
    /// </summary>
    public event Action AfterDisconnect;

    /// <summary>
    /// Event: Fires after the client successfully connected to the server
    /// (including the initial connect and every successful reconnect).
    /// </summary>
    public event Action AfterConnect;

    /// <summary>
    /// Event: Fires after the client reconnected following an unexpected loss of
    /// connection (e.g. a server restart). Provides information about whether the
    /// session was resumed and whether event subscriptions were restored.
    /// </summary>
    public event EventHandler<ReconnectedEventArgs> Reconnected;

    /// <summary>
    /// Event: Fires when the client detected that its event subscriptions could not
    /// be resynchronized with the server after a reconnect.
    /// </summary>
    public event EventHandler<SubscriptionDesyncEventArgs> SubscriptionsDesyncDetected;

    #endregion

    #region Construction

    private RemotingClient()
    {
        MethodCallMessageBuilder = new MethodCallMessageBuilder();
        MessageEncryptionManager = new MessageEncryptionManager();
        _authenticationRequired.Reset();
        _sharedSecret = null;
        _activeCalls = null;
        _activeCallsLock = new();
        _channelLock = new();
        _connectLock = new();
        _sessionLock = new();
        _currentlyPendingMessagesCounter = new(initialCount: 1);
        _delegateRegistry = new();
        _subscriptionTracker = new();
        _handshakeCompletedTaskSource = new();
        _authenticationResponseTaskSource = new();
        _authenticationCompletedTaskSource = new();
        _goodbyeCompletedEvent = new();
        _subscriptionInvoker = new(() => CreateInvoker<IServerSubscriptionService>());
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
        ProxyBuilder = config.ProxyBuilder ?? new RemotingProxyBuilder();

        _config = config;

        _keyPair = config.PrivateKeyBlob != null
            ? SessionKeyPairFactory.FromPrivateKey(config.PrivateKeyBlob)
            : SessionKeyPairFactory.Generate(MessageEncryption, config.KeySize);

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

        DefaultRemotingClient ??= this;
    }

    private void OnDisconnected()
    {
        // the connection is down: stop the heartbeat so it doesn't keep pinging
        // a dead transport and trigger redundant teardowns
        StopKeepSessionAliveTimer();

        Dictionary<Guid, ClientRpcContext> activeCalls;

        // the swap must happen under the same lock the RPC registration path uses,
        // otherwise a call registered right after the snapshot below would never
        // be completed and would hang until the invocation timeout
        using (_activeCallsLock.Lock())
        {
            activeCalls = _activeCalls;
            _activeCalls = null;
        }

        _goodbyeCompletedEvent.Set();

        var graceful = _sessionClosedByServer.Value;
        _sessionClosedByServer.Reset();

        // an unexpected loss of connection may trigger the auto-reconnect loop
        // (suppressed for manual disconnects and during disposal)
        EnsureAutoReconnectLoop();

        if (activeCalls == null)
            return;

        foreach (var activeCall in activeCalls)
        {
            if (graceful)
            {
                // Session was closed gracefully by server.
                // Complete all pending calls without error to allow graceful shutdown of in-flight calls
                activeCall.Value.Error = false;
                activeCall.Value.RemoteException = null;
                activeCall.Value.TaskSource.TrySetResult(null);
            }
            else
            {
                // Unexpected disconnect: mark calls as failed
                activeCall.Value.Error = true;
                activeCall.Value.RemoteException = new RemoteInvocationException("Server Disconnected");
                activeCall.Value.TaskSource.TrySetResult(null);
            }
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets an utility class to create remoting proxies.
    /// </summary>
    internal RemotingProxyBuilder ProxyBuilder { get; set; }

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
    public ISerializerAdapter Serializer { get; }

    /// <summary>
    /// Gets the local client delegate registry.
    /// </summary>
    internal ClientDelegateRegistry ClientDelegateRegistry => _delegateRegistry;

    /// <summary>
    /// Gets the client-side subscription tracker.
    /// </summary>
    internal SubscriptionTracker SubscriptionTracker => _subscriptionTracker;

    /// <summary>
    /// Gets whether this client instance is being disposed.
    /// This is a point-in-time snapshot for guard checks only. To enter the
    /// dispose path, use <c>_isDisposing.TrySet()</c>; combining this property
    /// with <c>Set()</c> is racy.
    /// </summary>
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    internal bool IsDisposing => _isDisposing.Value;

    /// <summary>
    /// Gets whether authentication of this client instance succeeded.
    /// </summary>
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    internal bool IsAuthenticated => _isAuthenticated.Value;

    /// <summary>
    /// Gets whether the server requested authentication for this connection.
    /// </summary>
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    internal bool AuthenticationRequired => _authenticationRequired.Value;

    /// <summary>
    /// Internal hook for background-operation failures. By default writes
    /// to <see cref="Trace"/>. Tests may replace it to assert failures.
    /// </summary>
    internal Action<Exception> ErrorHandler { get; set; } =
        ex => Trace.TraceWarning($"CoreRemoting client error: {ex}");

    /// <summary>
    /// Gets the number of subscriptions whose server-side removal is still
    /// in flight. Exposed for tests only.
    /// </summary>
    internal int PendingUnsubscribeCount => _pendingUnsubscribes.Count;

    /// <summary>
    /// Gets the number of currently pending messages. Exposed for tests only.
    /// </summary>
    internal int PendingMessageCount => (int)_currentlyPendingMessagesCounter.CurrentCount;

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
    /// Gets the private key of this CoreRemoting client instance (for persisting the key between process restarts to enable session resume).
    /// </summary>
    public byte[] PrivateKey => _keyPair?.PrivateKey;

    /// <summary>
    /// Gets the ID of the current session (null, if no session has been established).
    /// </summary>
    public Guid? SessionId
    {
        get
        {
            if (IsDisposing)
                return null;

            lock (_sessionLock)
                return _sessionId == Guid.Empty ? null : _sessionId;
        }
    }

    /// <summary>
    /// Gets the ID of a session that should be resumed on connection
    /// (the ID of the current session, or ClientConfig.ResumableSessionId, if no session is active).
    /// </summary>
    public Guid? ResumableSessionId
    {
        get
        {
            if (IsDisposing)
                return null;

            lock (_sessionLock)
            {
                if (_sessionId != Guid.Empty)
                    return _sessionId;

                return _rememberedSessionId != Guid.Empty
                    ? _rememberedSessionId
                    : _config.ResumableSessionId;
            }
        }
    }

    /// <summary>
    /// Gets the resumable session signature to verify client authenticity.
    /// </summary>
    public byte[] SessionSignature =>
        ResumableSessionId?.ToByteArray() is byte[] sessionId
            ? _keyPair.CreateSignature(sessionId)
            : null;

    /// <summary>
    /// Gets the currently used key size.
    /// </summary>
    internal int KeySize => _keyPair.KeySize;

    /// <summary>
    /// Gets the currently used shared key size.
    /// </summary>
    internal int SharedKeySize => _sharedSecretLength * 8;

    /// <summary>
    /// Gets whether the connection to the server is established or not.
    /// </summary>
    /// <remarks>
    /// Note: this is the transport state, not the client's logical state —
    /// see the internal <see cref="_isConnected"/> flag for the latter.
    /// </remarks>
    public bool IsConnected => _channel?.IsConnected ?? false;

    /// <summary>
    /// Gets whether this CoreRemoting client instance has a session or not.
    /// </summary>
    public bool HasSession
    {
        get
        {
            if (IsDisposing)
                return false;

            lock (_sessionLock)
                return _sessionId != Guid.Empty;
        }
    }


    /// <summary>
    /// Gets the authenticated identity. May be null if authentication failed or if authentication is not configured.
    /// </summary>
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public RemotingIdentity Identity { get; private set; }

    /// <summary>
    /// Gets the handshake metadata message for the communication channel.
    /// </summary>
    public ClientHandshakeMessage HandshakeMessage => new()
    {
        MessageEncryption = MessageEncryption,
        ClientPublicKey = PublicKey,
        SharedKeySize = Config.SharedKeySize,
        ResumableSessionId = ResumableSessionId,
        SessionSignature = SessionSignature,
    };

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
        if (IsConnected && HasSession)
            return;

        ReconnectedEventArgs reconnectedArgs = null;

        using (await _connectLock)
        {
            if (IsConnected && HasSession)
                return;

            if (_channel == null)
                throw new RemotingException("No client channel configured.");

            // an explicit connect ends any manual-disconnect suppression of auto-reconnect
            _manualDisconnect.Reset();

            _isConnected.Set();
            _goodbyeCompletedEvent.Reset();

            // reset heartbeat bookkeeping for the new connection
            _pingPending.Reset();
            Interlocked.Exchange(ref _missedPongs, 0);

            using (await _activeCallsLock)
                _activeCalls = new();

            // prepare a fresh handshake/authentication cycle. This is also required for session resume,
            // where the previous cycle's task sources are already completed and authentication must run again
            _handshakeCompletedTaskSource = new();
            _authenticationCompletedTaskSource = new();
            _isAuthenticated.Reset();
            Identity = null;

            // remember what we are trying to resume, so we can tell after the handshake
            // whether the session was actually resumed
            var requestedSessionId = ResumableSessionId;
            var isReconnect = _hasConnectedBefore;

            // clear any stale transport error left over from a previous connection,
            // so a reconnect isn't failed by the old socket's disconnect error
            if (_channel.RawMessageTransport != null)
                _channel.RawMessageTransport.LastException = null;

            try
            {
                await _channel.ConnectAsync()
                    .ConfigureAwait(false);

                if (_channel.RawMessageTransport.LastException != null)
                    throw _channel.RawMessageTransport.LastException;

                await _handshakeCompletedTaskSource.Task.Timeout(
                    _config.ConnectionTimeout, () =>
                        throw new NetworkException("Handshake with server failed."))
                .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // roll back the logical connection state, so a failed connect
                // doesn't leave _isConnected set without a live channel/session
                await DisconnectCoreAsync(quiet: true)
                    .ConfigureAwait(false);

                throw;
            }

            try
            {
                await AuthenticateAsync()
                    .ConfigureAwait(false);
            }
            catch (SecurityException)
            {
                // an explicit authentication failure is a deterministic rejection:
                // suppress the background auto-reconnect and require an explicit
                // Connect retry (which resets the suppression)
                _manualDisconnect.Set();

                await DisconnectCoreAsync(quiet: true)
                    .ConfigureAwait(false);

                throw;
            }
            catch (Exception)
            {
                await DisconnectCoreAsync(quiet: true)
                    .ConfigureAwait(false);

                throw;
            }

            // re-establish event subscriptions that the new connection (or a new
            // server instance) doesn't know about yet
            var subscriptionsRestored =
                await RestoreSubscriptionsAsync()
                    .ConfigureAwait(false);

            StartKeepSessionAliveTimer();

            _hasConnectedBefore = true;

            if (isReconnect)
            {
                var sessionResumed =
                    requestedSessionId.HasValue &&
                    SessionId == requestedSessionId.Value;

                reconnectedArgs = new ReconnectedEventArgs(
                    sessionResumed: sessionResumed,
                    sessionId: SessionId ?? Guid.Empty,
                    subscriptionsRestored: subscriptionsRestored);
            }
        }

        AfterConnect?.Invoke();

        if (reconnectedArgs != null)
            Reconnected?.Invoke(this, reconnectedArgs);
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
        // a manual disconnect suppresses automatic reconnection
        _manualDisconnect.Set();
        CancelAutoReconnect();

        // A graceful disconnect (goodbye sent) ends the session on the server, so the
        // next connect must start fresh instead of requesting a resume of a session
        // that no longer exists. A quiet disconnect leaves the session parked and
        // resumable, so the remembered session id is kept in that case.
        if (!quiet)
            lock (_sessionLock)
                _rememberedSessionId = Guid.Empty;

        using (await _connectLock)
            await DisconnectCoreAsync(quiet)
                .ConfigureAwait(false);

        AfterDisconnect?.Invoke();
    }

    private async Task DisconnectCoreAsync(bool quiet = false)
    {
        if (!_isConnected.TryReset())
            return;

        _currentlyPendingMessagesCounter.Signal();

        await _currentlyPendingMessagesCounter.WaitAsync()
            .ExpireMs(_config.WaitTimeForCurrentlyProcessedMessagesOnDispose)
                .ConfigureAwait(false);

        if (_channel == null)
            return;

        Guid sessionId;
        byte[] sessionKey;
        lock (_sessionLock)
        {
            if (_sessionId == Guid.Empty)
                return;
            sessionId = _sessionId;
            sessionKey = _sharedSecret;
            _sessionId = Guid.Empty;
            _sharedSecret = null;
        }

        StopKeepSessionAliveTimer();

        var sharedSecret =
            MessageEncryption
                ? sessionKey ?? sessionId.ToByteArray()
                : null;

        if (!quiet && _channel.IsConnected)
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

            var rawData = Serializer.Serialize(wireMessage);

            _goodbyeCompletedEvent.Reset();

            try
            {
                await _channel.RawMessageTransport.SendMessageAsync(rawData)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // ignored
                // TODO: dispatch the exception
            }

            await _goodbyeCompletedEvent.WaitAsync().ExpireMs(_config.WaitTimeForGoodbyeOnDisconnect)
                .ConfigureAwait(false);
        }

        using (await _channelLock)
        {
            if (_channel is IClientChannel channel)
                await channel.DisconnectAsync()
                    .ConfigureAwait(false);
        }

        OnDisconnected();
        _handshakeCompletedTaskSource = new();
        _authenticationCompletedTaskSource = new();
        Identity = null;
    }

    /// <summary>
    /// Kills the client transport without sending a goodbye message (simulates network failure).
    /// </summary>
    /// <param name="delayMs">Delay, in milliseconds.</param>
    /// <remarks>
    /// This method is used for unit testing only.
    /// </remarks>
    internal async Task HardKill(int delayMs = 200)
    {
        // simulate network failure
        await _channel.DisconnectAsync()
            .ConfigureAwait(false);

        // wait for the server-side disconnect handling (parking) to complete
        await Task.Delay(delayMs)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Simulates server disconnect race, exposed for unit tests only.
    /// </summary>
    internal void SimulateDisconnectRace()
    {
        using (_activeCallsLock.Lock())
            _activeCalls = null;
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
    /// Stops and disposes the keep session alive timer, if it is running.
    /// </summary>
    private void StopKeepSessionAliveTimer()
    {
        if (_keepSessionAliveTimer == null)
            return;

        _keepSessionAliveTimer.Stop();
        _keepSessionAliveTimer.Dispose();
        _keepSessionAliveTimer = null;
    }

    /// <summary>
    /// Event procedure: Called when the keep session alive timer elapses.
    /// Sends a ping message to the server and detects a dead connection
    /// when a previously sent ping was never answered with a pong.
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="e">Event arguments</param>
    private async void KeepSessionAliveTimerOnElapsed(object sender, ElapsedEventArgs e)
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

        // The ping sent on the previous tick was never answered: the connection
        // is half-open (e.g. the server crashed without closing the socket).
        // After two consecutive missed pongs, treat the connection as lost.
        if (_pingPending.Value)
        {
            if (Interlocked.Increment(ref _missedPongs) >= 2)
            {
                HandleConnectionLost();
                return;
            }
        }

        try
        {
            var pingMessage =
                MessageEncryptionManager.CreateWireMessage(
                    messageType: "ping",
                    serializer: Serializer,
                    serializedMessage: Array.Empty<byte>(),
                    keyPair: _keyPair,
                    sharedSecret: _sharedSecret);

            _pingPending.Set();

            await _rawMessageTransport.SendMessageAsync(Serializer.Serialize(pingMessage))
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // ignored — missed-pong detection handles a dead connection
        }
    }

    #endregion

    #region Authentication

    async Task<AuthenticationResponseMessage> IAuthenticationProvider.Authenticate(AuthenticationRequestMessage authRequestMessage)
    {
        var wireMessage =
            MessageEncryptionManager.CreateWireMessage(
                messageType: "auth",
                serializer: Serializer,
                serializedMessage: Serializer.Serialize(authRequestMessage),
                keyPair: _keyPair,
                sharedSecret: _sharedSecret);

        var rawData = Serializer.Serialize(wireMessage);

        _rawMessageTransport.LastException = null;

        _authenticationResponseTaskSource = new();

        await _rawMessageTransport.SendMessageAsync(rawData)
            .ConfigureAwait(false);

        if (_rawMessageTransport.LastException != null)
            throw _rawMessageTransport.LastException;

        return await _authenticationResponseTaskSource.Task;
    }

    /// <summary>
    /// Authenticates this CoreRemoting client instance with the specified credentials.
    /// </summary>
    /// <exception cref="SecurityException">Thrown, if authentication failed or timed out</exception>
    private async Task AuthenticateAsync()
    {
        if (_config.Credentials is null or { Length: 0 } &&
            _config.Authenticator is null or DefaultAuthenticator)
        {
            if (AuthenticationRequired)
                throw new SecurityException("Authentication is required. Please check credentials.");

            return;
        }

        if (_authenticationCompletedTaskSource.Task.IsCompleted)
            return;

        var authenticator = _config.Authenticator ?? new DefaultAuthenticator();

        var authResponse = authenticator.Authenticate(_config.Credentials, this);

        await Task.WhenAll(authResponse, _authenticationCompletedTaskSource.Task).Timeout(
            _config.AuthenticationTimeout, () =>
                throw new SecurityException("Authentication timeout."))
                    .ConfigureAwait(false);

        if (!IsAuthenticated)
            throw new SecurityException("Authentication failed. Please check credentials.");

        // if the authentication protocol negotiated a new shared key, re-key the session.
        // Both endpoints switch to the negotiated key right after this final response,
        // so the next message is already encrypted with it on both sides.
        var authResponseMessage = await authResponse.ConfigureAwait(false);
        if (MessageEncryption && authResponseMessage is { NegotiatedSharedKey.ContainsKeyMaterial: true })
        {
            var inputKeyMaterial = authResponseMessage.NegotiatedSharedKey.InputKeyMaterial;
            var derivedSharedKey = _config.HkdfProvider.DeriveKey(inputKeyMaterial, _sharedSecretLength, _sessionId, nameof(CoreRemoting));
            lock (_sessionLock)
                _sharedSecret = derivedSharedKey;
        }
    }

    #endregion

    #region Handling received messages

    /// <summary>
    /// Called when a message is received from server.
    /// </summary>
    /// <param name="rawMessage">Raw message data</param>
    private void OnMessage(byte[] rawMessage)
    {
        var message = TryDeserialize(rawMessage);

        // Set flag synchronously before dispatching to Task.Run,
        // to avoid a race between ProcessSessionClosedMessage and
        // the Disconnected event (which fires synchronously from
        // WatsonTcp after the Shutdown message is received).
        if (string.Equals(message.MessageType, "session_closed", StringComparison.OrdinalIgnoreCase))
            _sessionClosedByServer.Set();

        Task.Run(async () =>
        {
            switch (message.MessageType.ToLower())
            {
                case "complete_handshake":
                    ProcessCompleteHandshakeMessage(message);
                    break;
                case "auth_response":
                    ProcessAuthenticationResponseMessage(message);
                    break;
                case "rpc_result":
                    await ProcessRpcResultMessage(message);
                    break;
                case "invoke":
                    ProcessRemoteDelegateInvocationMessage(message);
                    break;
                case "goodbye":
                    ProcessGoodbyeMessage(message);
                    break;
                case "pong":
                    ProcessPongMessage();
                    break;
                case "session_closed":
                    await ProcessSessionClosedMessage(message);
                    break;
                default:
                    // TODO: how do we handle invalid wire messages received by the client?
                    // A wire message could have been tampered with and couldn't be deserialized
                    break;
            }
        }).ConfigureAwait(false);
    }

    private WireMessage TryDeserialize(byte[] rawMessage)
    {
        WireMessage getInvalidMessage() => new()
        {
            Data = rawMessage,
            Error = true,
            Iv = [],
            MessageType = "invalid",
            UniqueCallKey = [],
        };

        try
        {
            return Serializer.Deserialize<WireMessage>(rawMessage) ??
                getInvalidMessage();
        }
        catch // TODO: dispatch message deserialization exception?
        {
            return getInvalidMessage();
        }
    }

    /// <summary>
    /// Processes a complete handshake message from server.
    /// </summary>
    /// <param name="message">Deserialized WireMessage that contains a plain or encrypted Session ID</param>
    private void ProcessCompleteHandshakeMessage(WireMessage message)
    {
        var handshakeSecret = message.Data;

        if (MessageEncryption)
        {
            var signedMessageData =
                Serializer.Deserialize<SignedMessageData>(message.Data);

            var encryptedSecret =
                Serializer.Deserialize<EncryptedSecret>(signedMessageData.MessageRawData);

            _serverPublicKeyBlob = encryptedSecret.SendersPublicKeyBlob;

            using var verifier =
                SessionKeyPairFactory.FromPublicKey(_serverPublicKeyBlob);

            if (!verifier.VerifySignature(
                data: signedMessageData.MessageRawData,
                signature: signedMessageData.Signature))
                throw new SecurityException("Verification of message signature failed.");

            handshakeSecret = RsaKeyExchange.DecryptSecret(
                keySize: _config.KeySize,
                // ReSharper disable once PossibleNullReferenceException
                receiversPrivateKeyBlob: _keyPair.PrivateKey,
                encryptedSecret: encryptedSecret);
        }

        // The handshake secret contains the session ID and, unless the server uses
        // legacy session key derivation, an additional symmetric session key
        var handshakeMessage = handshakeSecret.Length > 16 ?
            Serializer.Deserialize<CompleteHandshakeMessage>(handshakeSecret) :
            new CompleteHandshakeMessage
            {
                SessionId = new Guid(handshakeSecret),
            };

        // Capture the session the client requested to resume. This is the previously
        // remembered session (or the one explicitly set in ClientConfig for a
        // cross-process resume). It must be read before _sessionId is overwritten
        // below, otherwise a refused resume would be masked by the new session ID.
        var requestedSessionId =
            _rememberedSessionId != Guid.Empty
                ? _rememberedSessionId
                : _config.ResumableSessionId;

        lock (_sessionLock)
        {
            _sessionId = handshakeMessage.SessionId;
            _sharedSecret = MessageEncryption ? handshakeMessage.SharedSecret ?? _sessionId.ToByteArray() : null;
            _sharedSecretLength = _sharedSecret?.Length ?? 0;
            _authenticationRequired.Set(handshakeMessage.AuthenticationRequired);
        }

        // server requires message encryption, but the client's connection is unencrypted
        if (handshakeMessage.MessageEncryptionRequired && !MessageEncryption)
        {
            var exception = new SecurityException("RemotingServer requires message encryption.");
            _handshakeCompletedTaskSource.TrySetException(exception);
            throw exception;
        }

        // The client explicitly requested to resume a specific session, but the server
        // created a new session instead. Whether the client may silently fall back to
        // this new session is decided by the SERVER (ServerConfig.AllowSessionResumeFallback),
        // communicated here in the handshake. The default is OFF, so a refused resume
        // keeps throwing — this preserves the previous (backward compatible) behavior.
        if (requestedSessionId != null &&
            _sessionId != requestedSessionId.Value &&
            !handshakeMessage.AllowSessionResumeFallback)
        {
            var exception = new RemotingException(
                $"Server refused to resume the requested session {requestedSessionId.Value}.");

            _handshakeCompletedTaskSource.TrySetException(exception);
            throw exception;
        }

        // Remember the session that is now active, so that future reconnects
        // (after an unexpected disconnect) automatically request its resume.
        // Stored in a private field (not the shared ClientConfig) so that a config
        // instance shared across multiple clients cannot cross-contaminate resume identity.
        lock (_sessionLock)
            _rememberedSessionId = _sessionId;

        _handshakeCompletedTaskSource.TrySetResult(true);
    }

    /// <summary>
    /// Processes a authentication response message from server.
    /// </summary>
    /// <param name="message">Deserialized WireMessage that contains a AuthenticationResponseMessage</param>
    private void ProcessAuthenticationResponseMessage(WireMessage message)
    {
        var decryptedData =
            MessageEncryptionManager.GetDecryptedMessageData(
                message: message,
                serializer: Serializer,
                sharedSecret: _sharedSecret,
                sendersPublicKeyBlob: _serverPublicKeyBlob);

        var authResponseMessage =
            Serializer
                .Deserialize<AuthenticationResponseMessage>(decryptedData);

        _authenticationResponseTaskSource.TrySetResult(authResponseMessage);

        if (authResponseMessage.IsCompleted)
        {
            _isAuthenticated.Set(authResponseMessage.IsAuthenticated);
            Identity = authResponseMessage.IsAuthenticated ? authResponseMessage.AuthenticatedIdentity : null;
            _authenticationCompletedTaskSource.TrySetResult(true);
        }
    }

    /// <summary>
    /// Processes a goodbye message.
    /// </summary>
    private void ProcessGoodbyeMessage(WireMessage message) =>
        _goodbyeCompletedEvent.Set();

    /// <summary>
    /// Processes a session_closed message.
    /// </summary>
    private async Task ProcessSessionClosedMessage(WireMessage message)
    {
        // ignore a stale close notification:
        // - transport already down, or
        // - no active session (mid-disconnect or handshake in progress)
        if (!IsConnected || !_isConnected.Value || !HasSession)
            return;

        // mark that the session was closed by server to gracefully complete in-flight calls
        _sessionClosedByServer.Set();

        // The server explicitly closed this session, so it is gone (not parked).
        // Forget the resume identity so a later reconnect starts a fresh session
        // instead of trying to resume a session the server already removed.
        lock (_sessionLock)
            _rememberedSessionId = Guid.Empty;

        // Note: this is intentionally NOT treated as a manual disconnect, so that
        // with AutoReconnect enabled the client re-establishes the connection
        // after the server closed the session.
        using (await _connectLock)
            await DisconnectCoreAsync(quiet: true)
                .ConfigureAwait(false);

        AfterDisconnect?.Invoke();
    }

    /// <summary>
    /// Reports a background-operation failure through <see cref="ErrorHandler"/>.
    /// </summary>
    private void OnError(Exception ex) => ErrorHandler?.Invoke(ex);

    /// <summary>
    /// Notifies the server that the given handlers should be removed.
    /// Awaitable; propagates RPC failures to the caller.
    /// </summary>
    internal async Task NotifyServerUnsubscribeAsync(Guid[] handlerKeys)
    {
        if (handlerKeys is null or { Length: 0 })
            return;

        if (!_isConnected.Value)
            return;

        await _subscriptionInvoker.Value
            .Invoke(s => s.RemoveHandlers(handlerKeys))
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Schedules server-side removal of the given handlers.
    /// Deduplicated on a best-effort basis: if a removal for the same key is
    /// already in flight, the existing task is reused. Under a rare race two
    /// RPCs may be issued for the same key — harmless, because
    /// <c>IServerSubscriptionService.RemoveHandlers</c> is idempotent.
    /// Errors are reported via <see cref="ErrorHandler"/>, not propagated.
    /// </summary>
    internal Task ScheduleServerUnsubscribe(Guid[] handlerKeys)
    {
        if (handlerKeys is null or { Length: 0 })
            return Task.CompletedTask;

        var tasks = new Task[handlerKeys.Length];

        for (var i = 0; i < handlerKeys.Length; i++)
            tasks[i] = _pendingUnsubscribes.GetOrAdd(handlerKeys[i], StartUnsubscribe);

        return Task.WhenAll(tasks);
    }

    private Task StartUnsubscribe(Guid key) => Task.Run(async () =>
    {
        try
        {
            await NotifyServerUnsubscribeAsync([key]).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
        finally
        {
            _pendingUnsubscribes.TryRemove(key, out _);
        }
    });

    /// <summary>
    /// Processes a remote delegate invocation message from server.
    /// </summary>
    /// <param name="message">Deserialized WireMessage that contains a RemoteDelegateInvocationMessage</param>
    private void ProcessRemoteDelegateInvocationMessage(WireMessage message)
    {
        if (_goodbyeCompletedEvent.IsSet)
            return;

        var delegateInvocationMessage =
            Serializer
                .Deserialize<RemoteDelegateInvocationMessage>(
                    MessageEncryptionManager.GetDecryptedMessageData(
                        message: message,
                        serializer: Serializer,
                        sharedSecret: _sharedSecret,
                        sendersPublicKeyBlob: _serverPublicKeyBlob));

        var localDelegate = _delegateRegistry.GetDelegateByHandlerKey(
            delegateInvocationMessage.HandlerKey);

        if (localDelegate == null)
        {
            // handler was unregistered locally, tell the server to drop
            // inactive subscription, so it stops sending invocations for this key
            _ = ScheduleServerUnsubscribe([delegateInvocationMessage.HandlerKey]);
            return;
        }

        // invoke local delegate with arguments from remote caller
        ServiceEventHub.DelegateInvoker.Invoke(localDelegate, delegateInvocationMessage.DelegateArguments);
    }

    /// <summary>
    /// Processes a RPC result message from server.
    /// </summary>
    /// <param name="message">Deserialized WireMessage that contains a MethodCallResultMessage or a RemoteInvocationException</param>
    /// <exception cref="KeyNotFoundException">Thrown, when the received result is of a unknown call</exception>
    private async Task ProcessRpcResultMessage(WireMessage message)
    {
        // decrease the counter when finished processing
        using var signal = Disposable.Create(() =>
            _currentlyPendingMessagesCounter.Signal());

        if (_goodbyeCompletedEvent.IsSet)
            return;

        Guid unqiueCallKey =
            message.UniqueCallKey == null
                ? Guid.Empty
                : new Guid(message.UniqueCallKey);

        ClientRpcContext clientRpcContext;

        using (await _activeCallsLock)
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
                            sharedSecret: _sharedSecret,
                            sendersPublicKeyBlob: _serverPublicKeyBlob));

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
                        sharedSecret: _sharedSecret,
                        sendersPublicKeyBlob: _serverPublicKeyBlob);

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

    #region Subscription recovery and auto reconnect

    /// <summary>
    /// Gets the client-side subscription replay registry.
    /// </summary>
    internal SubscriptionReplayRegistry SubscriptionReplay => _subscriptionReplay;

    /// <summary>
    /// Tracks an event accessor call for subscription bookkeeping and replay.
    /// Called by service proxies and invokers on every intercepted call.
    /// </summary>
    /// <param name="serviceName">Name of the remote service owning the event.</param>
    /// <param name="method">The intercepted <c>add_*</c> or <c>remove_*</c> accessor method.</param>
    /// <param name="mappedArgs">Mapped arguments of the accessor call.</param>
    internal void TrackSubscriptionCall(string serviceName, MethodInfo method, object[] mappedArgs)
    {
        var handlerKey = SubscriptionTracker.ExtractHandlerKey(mappedArgs);

        _subscriptionTracker.Track(method, handlerKey);
        _subscriptionReplay.Track(serviceName, method, mappedArgs, handlerKey);
    }

    /// <summary>
    /// Processes a pong message from the server: the connection is alive.
    /// </summary>
    private void ProcessPongMessage()
    {
        _pingPending.Reset();
        Interlocked.Exchange(ref _missedPongs, 0);
    }

    /// <summary>
    /// Verifies the server's subscription state against the client's tracker and
    /// replays all stored subscriptions if they differ.
    /// </summary>
    /// <returns>True if no replay was needed or the replay verified successfully; otherwise false.</returns>
    private async Task<bool> RestoreSubscriptionsAsync()
    {
        if (_subscriptionReplay.Count == 0)
            return true;

        try
        {
            var clientChecksum = _subscriptionTracker.Checksum;
            var serverChecksum =
                await GetServerSubscriptionChecksumAsync()
                    .ConfigureAwait(false);

            if (serverChecksum == clientChecksum)
                return true;

            await ResubscribeAllAsync()
                .ConfigureAwait(false);

            clientChecksum = _subscriptionTracker.Checksum;
            serverChecksum =
                await GetServerSubscriptionChecksumAsync()
                    .ConfigureAwait(false);

            if (serverChecksum != clientChecksum)
            {
                SubscriptionsDesyncDetected?.Invoke(this,
                    new SubscriptionDesyncEventArgs(clientChecksum, serverChecksum));

                OnError(new RemotingException(
                    $"Subscription resynchronization failed (client: {clientChecksum}, server: {serverChecksum})."));

                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            OnError(ex);
            return false;
        }
    }

    /// <summary>
    /// Replays all stored event subscriptions on the current connection.
    /// The replayed <c>add_*</c> calls are not re-tracked on the client side,
    /// because the tracker already accounts for them.
    /// </summary>
    private async Task ResubscribeAllAsync()
    {
        foreach (var entry in _subscriptionReplay.Snapshot())
        {
            for (var i = 0; i < entry.Count; i++)
            {
                var message =
                    MethodCallMessageBuilder.BuildMethodCallMessage(
                        serializer: Serializer,
                        remoteServiceName: entry.ServiceName,
                        targetMethod: entry.AddMethod,
                        args: entry.MappedArgs);

                await InvokeRemoteMethod(message)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Asks the server for the subscription checksum of the current session.
    /// </summary>
    private async Task<string> GetServerSubscriptionChecksumAsync()
    {
        return await _subscriptionInvoker.Value
            .Invoke(s => s.GetSubscriptionChecksum())
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Handles a connection that was detected as dead by the heartbeat:
    /// tears the connection down quietly and starts the auto-reconnect loop.
    /// </summary>
    private void HandleConnectionLost()
    {
        if (!_isConnected.Value)
            return;

        _pingPending.Reset();
        Interlocked.Exchange(ref _missedPongs, 0);

        _ = Task.Run(async () =>
        {
            try
            {
                using (await _connectLock)
                    await DisconnectCoreAsync(quiet: true)
                        .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
            finally
            {
                AfterDisconnect?.Invoke();
                EnsureAutoReconnectLoop();
            }
        });
    }

    /// <summary>
    /// Starts the background auto-reconnect loop, unless auto-reconnect is disabled,
    /// the client is disposing, a manual disconnect is in effect, or a loop is
    /// already running.
    /// </summary>
    private void EnsureAutoReconnectLoop()
    {
        if (!_config.AutoReconnect ||
            _isDisposing.Value ||
            _manualDisconnect.Value)
            return;

        if (Interlocked.CompareExchange(ref _reconnectLoopActive, 1, 0) != 0)
            return;

        _ = Task.Run(RunAutoReconnectLoopAsync);
    }

    private async Task RunAutoReconnectLoopAsync()
    {
        var cancellationToken = _reconnectCts.Token;

        try
        {
            var delaySeconds = Math.Max(1, _config.ReconnectMinDelay);
            var maxDelaySeconds = Math.Max(delaySeconds, _config.ReconnectMaxDelay);

            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken)
                    .ConfigureAwait(false);

                if (_isDisposing.Value || _manualDisconnect.Value)
                    return;

                if (IsConnected && HasSession)
                    return;

                try
                {
                    await ConnectAsync()
                        .ConfigureAwait(false);

                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    OnError(ex);
                    delaySeconds = Math.Min(maxDelaySeconds, delaySeconds * 2);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // loop cancelled — normal shutdown path
        }
        finally
        {
            Interlocked.Exchange(ref _reconnectLoopActive, 0);
        }
    }

    /// <summary>
    /// Cancels a running auto-reconnect loop (if any) and prepares a fresh
    /// cancellation token source for future reconnects.
    /// </summary>
    private void CancelAutoReconnect()
    {
        Interlocked.Exchange(ref _reconnectCts, new CancellationTokenSource())
            ?.Cancel();
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
        if (!IsConnected)
            await ConnectAsync()
                .ConfigureAwait(false);

        var signalCount = oneWay ? 0 : 1;
        _currentlyPendingMessagesCounter.AddCount(signalCount);

        if (!_isConnected.Value)
        {
            _currentlyPendingMessagesCounter.Signal(signalCount);
            throw new RemoteInvocationException("Client disconnected");
        }

        ClientRpcContext clientRpcContext;

        // the null check and the registration must be atomic: a disconnect that
        // lands between two separate lock regions would either null the
        // dictionary (NullReferenceException on Add) or register the call in a
        // dead dictionary that nobody ever completes
        using (await _activeCallsLock)
        {
            if (_activeCalls == null)
            {
                _currentlyPendingMessagesCounter.Signal(signalCount);
                throw new RemoteInvocationException("Server disconnected");
            }

            clientRpcContext = new ClientRpcContext();
            _activeCalls.Add(clientRpcContext.UniqueCallKey, clientRpcContext);
        }

        var wireMessage =
            MessageEncryptionManager.CreateWireMessage(
                messageType: "rpc",
                serializer: Serializer,
                serializedMessage: Serializer.Serialize(methodCallMessage),
                sharedSecret: _sharedSecret,
                keyPair: _keyPair,
                uniqueCallKey: clientRpcContext.UniqueCallKey.ToByteArray());

        var rawData = Serializer.Serialize(wireMessage);

        _rawMessageTransport.LastException = null;

        await _rawMessageTransport.SendMessageAsync(rawData)
            .ConfigureAwait(false);

        if (_rawMessageTransport.LastException != null)
        {
            using (await _activeCallsLock)
                _activeCalls.Remove(clientRpcContext.UniqueCallKey);

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
    public T CreateProxy<T>(string serviceName = "") =>
        ProxyBuilder.CreateProxy<T>(this, serviceName);

    /// <summary>
    /// Creates a proxy object to provide access to a remote service.
    /// </summary>
    /// <param name="serviceInterfaceType">Interface type of the remote service</param>
    /// <param name="serviceName">Unique name of the remote service</param>
    /// <returns>Proxy object</returns>
    public object CreateProxy(Type serviceInterfaceType, string serviceName = "")
    {
        var createMethodInfo = new Func<string, int>(CreateProxy<int>).Method.GetGenericMethodDefinition();
        var createProxyFunc = createMethodInfo.MakeGenericMethod([serviceInterfaceType]);
        return createProxyFunc.Invoke(this, [serviceName]);
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
    /// Creates a service invoker that provides access to a remote service without generating a proxy.
    /// </summary>
    /// <typeparam name="TService">Type of the remote service contract (interface or class)</typeparam>
    /// <param name="serviceName">Unique name of the remote service</param>
    /// <returns>Service invoker object</returns>
    public IServiceInvoker<TService> CreateInvoker<TService>(string serviceName = "")
        where TService : class =>
        new ServiceInvoker<TService>(this, serviceName);

    /// <summary>
    /// Shuts a specified service proxy down and frees resources.
    /// </summary>
    /// <remarks>
    /// The server-side removal of subscriptions registered through this proxy
    /// is scheduled asynchronously (fire-and-forget). Callers that need to await
    /// the server-side cleanup should use <see cref="ShutdownProxyAsync"/>.
    /// </remarks>
    /// <param name="serviceProxy">Proxy object that should be shut down</param>
    public void ShutdownProxy(object serviceProxy)
    {
        var keys = ShutdownLocal(serviceProxy);
        _ = ScheduleServerUnsubscribe(keys);
    }

    /// <summary>
    /// Shuts a specified service proxy down and frees resources, awaiting the
    /// server-side removal of its subscriptions.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="ShutdownProxy"/>, this method awaits the RPC that tells
    /// the server to drop the proxy's subscriptions. Use it when deterministic
    /// cleanup is required (e.g. in tests or before returning a proxy to a pool).
    /// </remarks>
    /// <param name="serviceProxy">Proxy object that should be shut down</param>
    public async Task ShutdownProxyAsync(object serviceProxy)
    {
        var keys = ShutdownLocal(serviceProxy);
        await NotifyServerUnsubscribeAsync(keys)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the CoreRemoting service proxy (interceptor) inside a Castle-generated
    /// proxy object. Returns <c>null</c> if the object is not a CoreRemoting proxy.
    /// </summary>
    internal static IServiceProxy FindServiceProxy(object serviceProxy)
    {
        if (!ProxyUtil.IsProxy(serviceProxy))
            return null;

        var proxyType = serviceProxy.GetType();

        var hiddenInterceptorsField =
            proxyType.GetField("__interceptors",
                BindingFlags.Instance | BindingFlags.NonPublic);

        if (hiddenInterceptorsField == null)
            return null;

        var interceptors = hiddenInterceptorsField.GetValue(serviceProxy) as IInterceptor[];

        return interceptors?
            .OfType<IServiceProxy>()
                .FirstOrDefault();
    }

    /// <summary>
    /// Performs local-only proxy shutdown: unregisters client delegates, calls
    /// <see cref="IServiceProxy.Shutdown"/>, and returns the unregistered keys
    /// so the caller can schedule/await the server-side removal.
    /// </summary>
    private Guid[] ShutdownLocal(object serviceProxy)
    {
        var proxy = FindServiceProxy(serviceProxy);
        if (proxy == null)
            return [];

        var keys = _delegateRegistry.UnregisterClientDelegatesOfServiceProxy(proxy);
        proxy.Shutdown();
        return keys;
    }

    #endregion

    #region IDisposable implementation

    /// <summary>
    /// Frees managed resources.
    /// </summary>
    public void Dispose() => DisposeAsync().JustWait();

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (!_isDisposing.TrySet())
            return;

        CancelAutoReconnect();

        if (DefaultRemotingClient == this)
            DefaultRemotingClient = null;

        _clientInstances.TryRemove(_config.UniqueClientInstanceName, out _);

        await DisconnectAsync()
            .ConfigureAwait(false);

        _handshakeCompletedTaskSource.TrySetCanceled();
        _authenticationResponseTaskSource.TrySetCanceled();
        _authenticationCompletedTaskSource.TrySetCanceled();

        await Task.WhenAll(_pendingUnsubscribes.Values)
            .ConfigureAwait(false);

        _delegateRegistry.Clear();
        _subscriptionReplay.Clear();

        if (_subscriptionInvoker.IsValueCreated)
        {
            _subscriptionInvoker.Value.Dispose();
        }

        if (_rawMessageTransport != null)
        {
            _rawMessageTransport.ReceiveMessage -= OnMessage;
            _rawMessageTransport = null;
        }

        using (await _channelLock)
        {
            if (_channel != null)
            {
                await _channel.DisposeAsync()
                    .ConfigureAwait(false);

                _channel = null;
            }
        }

        _keyPair?.Dispose();
        _activeCallsLock.Dispose();
        _connectLock.Dispose();
        _channelLock.Dispose();
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

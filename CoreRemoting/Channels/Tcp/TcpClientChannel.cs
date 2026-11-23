using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CoreRemoting.Channels.Common;
using CoreRemoting.Threading;
using WatsonTcp;

namespace CoreRemoting.Channels.Tcp;

/// <summary>
/// Client side TCP channel implementation.
/// </summary>
public class TcpClientChannel : MessageTransportBase, IClientChannel
{
    private IRemotingClient _client;
    private WatsonTcpClient _tcpClient;
    private Dictionary<string, object> _handshakeMetadata;

    private AsyncLock DisposeLock { get; } = new();

    /// <summary>
    /// Initializes the channel.
    /// </summary>
    /// <param name="client">CoreRemoting client</param>
    public void Init(IRemotingClient client)
    {
        _client = client;
        _tcpClient = new WatsonTcpClient(client.Config.ServerHostName, client.Config.ServerPort);
        _tcpClient.Settings.NoDelay = true;

        ApplyHandshakeMetadata(client);
    }

    /// <summary>
    /// Updates the handshake metadata with the encryption settings, 
    /// the ID of a session that should be resumed, and so on.
    /// </summary>
    /// <param name="client">CoreRemoting client</param>
    private void ApplyHandshakeMetadata(IRemotingClient client)
    {
        _handshakeMetadata = client.HandshakeMessage.Metadata
            .ToDictionary(kv => kv.Key, kv => kv.Value as object);
    }

    /// <summary>
    /// Establish a connection with the server.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (_tcpClient == null)
            throw new InvalidOperationException("Channel is not initialized.");

        if (_tcpClient.Connected)
            return;

        ApplyHandshakeMetadata(_client);

        // The channel instance is reused across reconnects, so make the event wiring
        // idempotent: detach any handlers left over from a previous connection before
        // attaching them again. Without this, every reconnect would add another
        // subscription and deliver each received message multiple times.
        _tcpClient.Events.ExceptionEncountered -= OnError;
        _tcpClient.Events.MessageReceived -= OnMessage;
        _tcpClient.Events.ServerDisconnected -= OnServerDisconnected;

        _tcpClient.Events.ExceptionEncountered += OnError;
        _tcpClient.Events.MessageReceived += OnMessage;
        // ServerDisconnected is intentionally attached only after the new
        // connection is fully established: a late teardown event from the
        // previous connection must not reach the client while the new
        // connection is being set up.

        _tcpClient.Connect();

        // note: we don't rely on the Connected event anymore
        await _tcpClient.SendAsync([0], _handshakeMetadata);

        _tcpClient.Events.ServerDisconnected += OnServerDisconnected;
    }

    private void OnServerDisconnected(object o, DisconnectionEventArgs disconnectionEventArgs)
    {
        OnDisconnected();
    }

    /// <summary>
    /// Event procedure: Called when a error occurs on the TCP client layer.
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="e">Event arguments</param>
    private void OnError(object sender, ExceptionEventArgs e)
    {
        LastException = new NetworkException(e.Exception.Message, e.Exception);

        OnErrorOccured(e.Exception.Message, e.Exception);
    }

    /// <summary>
    /// Event procedure: Called when a message from server is received.
    /// </summary>
    /// <param name="sender">Sender of the event</param>
    /// <param name="e">Event arguments containing the message content</param>
    private void OnMessage(object sender, MessageReceivedEventArgs e)
    {
        OnReceiveMessage(e.Data);
    }

    /// <summary>
    /// Closes the connection.
    /// </summary>
    public async Task DisconnectAsync()
    {
        using (await DisposeLock)
        {
            if (_tcpClient == null)
                return;

            // work around for double Dispose, see
            // https://github.com/dotnet/WatsonTcp/issues/316
            if (_tcpClient.Events != null)
            {
                _tcpClient.Events.MessageReceived -= OnMessage;
                _tcpClient.Events.ExceptionEncountered -= OnError;
            }

            if (_tcpClient.Connected)
            {
                try
                {
                    _tcpClient.Disconnect();
                }
                catch
                {
                    // ignored
                }
            }
        }
    }

    /// <summary>
    /// Gets whether the connection is established or not.
    /// </summary>
    public bool IsConnected => _tcpClient?.Connected ?? false;

    /// <summary>
    /// Gets the raw message transport component for this connection.
    /// </summary>
    public IRawMessageTransport RawMessageTransport => this;

    /// <inheritdoc />
    protected override async Task<bool> SendCoreAsync(byte[] rawMessage)
    {
        if (await _tcpClient.SendAsync(rawMessage).ConfigureAwait(false))
            return true;

        throw new NetworkException("Channel disconnected");
    }

    /// <summary>
    /// Disconnect and free manages resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync()
            .ConfigureAwait(false);
        
        using (await DisposeLock)
        {
            // work around for double Dispose, see
            // https://github.com/dotnet/WatsonTcp/issues/316
            try
            {
                _tcpClient.Dispose();
                _tcpClient = null;
            }
            catch
            {
                // ignored
            }
        }
        
        DisposeLock.Dispose();
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CoreRemoting.Threading;
using WatsonTcp;

namespace CoreRemoting.Channels.Tcp;

/// <summary>
/// Client side TCP channel implementation.
/// </summary>
public class TcpClientChannel : IClientChannel, IRawMessageTransport
{
    private WatsonTcpClient _tcpClient;
    private Dictionary<string, object> _handshakeMetadata;

    /// <summary>
    /// Event: Fires when a message is received from server.
    /// </summary>
    public event Action<byte[]> ReceiveMessage;

    /// <summary>
    /// Event: Fires when an error is occurred.
    /// </summary>
    public event Action<string, Exception> ErrorOccured;

    /// <inheritdoc />
    public event Action Disconnected;
    
    private AsyncLock DisposeLock { get; } = new();

    /// <summary>
    /// Initializes the channel.
    /// </summary>
    /// <param name="client">CoreRemoting client</param>
    public void Init(IRemotingClient client)
    {
        _tcpClient = new WatsonTcpClient(client.Config.ServerHostName, client.Config.ServerPort);
        _tcpClient.Settings.NoDelay = true;

        _handshakeMetadata = new()
        {
            { "MessageEncryption", client.MessageEncryption }
        };

        if (client.MessageEncryption)
            _handshakeMetadata.Add("ShakeHands", Convert.ToBase64String(client.PublicKey));
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

        _tcpClient.Events.ExceptionEncountered += OnError;
        _tcpClient.Events.MessageReceived += OnMessage;
        _tcpClient.Events.ServerDisconnected += OnDisconnected;
        _tcpClient.Connect();

        // note: we don't rely on the Connected event anymore
        await _tcpClient.SendAsync([0], _handshakeMetadata);
    }

    private void OnDisconnected(object o, DisconnectionEventArgs disconnectionEventArgs)
    {
        Disconnected?.Invoke();
    }

    /// <summary>
    /// Event procedure: Called when a error occurs on the TCP client layer.
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="e">Event arguments</param>
    private void OnError(object sender, ExceptionEventArgs e)
    {
        LastException = new NetworkException(e.Exception.Message, e.Exception);

        ErrorOccured?.Invoke(e.Exception.Message, e.Exception);
    }

    /// <summary>
    /// Event procedure: Called when a message from server is received.
    /// </summary>
    /// <param name="sender">Sender of the event</param>
    /// <param name="e">Event arguments containing the message content</param>
    private void OnMessage(object sender, MessageReceivedEventArgs e)
    {
        ReceiveMessage?.Invoke(e.Data);
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

    /// <summary>
    /// Sends a message to the server.
    /// </summary>
    /// <param name="rawMessage">Raw message data</param>
    public async Task<bool> SendMessageAsync(byte[] rawMessage)
    {
        try
        {
            if (await _tcpClient.SendAsync(rawMessage).ConfigureAwait(false))
                return true;
            throw new NetworkException("Channel disconnected");
        }
        catch (Exception ex)
        {
            LastException = ex as NetworkException ??
                            new NetworkException(ex.Message, ex);

            ErrorOccured?.Invoke(ex.Message, LastException);
            return false;
        }
    }

    /// <summary>
    /// Gets or sets the last exception.
    /// </summary>
    public NetworkException LastException { get; set; }

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

using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Initializes the channel.
    /// </summary>
    /// <param name="client">CoreRemoting client</param>
    public void Init(IRemotingClient client)
    {
        _tcpClient = new WatsonTcpClient(client.Config.ServerHostName, client.Config.ServerPort);
        _tcpClient.Settings.NoDelay = true;

        _handshakeMetadata = 
            new Dictionary<string, object>
            {
                { "MessageEncryption", client.MessageEncryption }
            };

        if (client.MessageEncryption)
            _handshakeMetadata.Add("ShakeHands", Convert.ToBase64String(client.PublicKey));
    }

    /// <summary>
    /// Establish a connection with the server.
    /// </summary>
    public void Connect()
    {
        if (_tcpClient == null)
            throw new InvalidOperationException("Channel is not initialized.");
            
        if (_tcpClient.Connected)
            return;

        _tcpClient.Events.ExceptionEncountered += OnError;
        _tcpClient.Events.MessageReceived += OnMessage;
        _tcpClient.Events.ServerDisconnected += OnDisconnected;
        _tcpClient.Connect();
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
        if (e.Metadata != null && e.Metadata.ContainsKey("ServerAcceptConnection"))
        {
            if (!_tcpClient.Send(new byte[1] { 0x0 }, _handshakeMetadata) && !_tcpClient.Connected)
                _tcpClient = null;
        }
        else
        {
            ReceiveMessage?.Invoke(e.Data);
        }
    }

    /// <summary>
    /// Closes the connection.
    /// </summary>
    public void Disconnect()
    {
        if (_tcpClient == null)
            return;

        _tcpClient.Events.MessageReceived -= OnMessage;
        _tcpClient.Events.ExceptionEncountered -= OnError;

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

        _tcpClient.Dispose();
        _tcpClient = null;
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
    public bool SendMessage(byte[] rawMessage)
    {
        if (_tcpClient != null)
        {
            if (_tcpClient.Send(rawMessage))
                return true;
            if (!_tcpClient.Connected)
                _tcpClient = null;
            return false;
        }
        else
            throw new NetworkException("Channel disconnected");
    }
    
    /// <summary>
    /// Gets or sets the last exception.
    /// </summary>
    public NetworkException LastException { get; set; }
    
    /// <summary>
    /// Disconnect and free manages resources.
    /// </summary>
    public void Dispose()
    {
        Disconnect();
    }
}

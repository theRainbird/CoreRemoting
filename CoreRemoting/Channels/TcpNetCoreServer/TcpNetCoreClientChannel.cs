using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace CoreRemoting.Channels.TcpNetCoreServer;

/// <summary>
/// Client side TCP channel implementation.
/// </summary>
public class TcpNetCoreClientChannel : IClientChannel, IRawMessageTransport
{
    private RemotingTcpClient _tcpClient;
    private Dictionary<string, object> _handshakeMetadata;

    /// <summary>
    /// Event: Fires when a message is received from server.
    /// </summary>
    public event Action<byte[]> ReceiveMessage;
        
    /// <summary>
    /// Event: Fires when an error is occurred.
    /// </summary>
    public event Action<string, Exception> ErrorOccured;

    /// <summary>
    /// Initializes the channel.
    /// </summary>
    /// <param name="client">CoreRemoting client</param>
    public void Init(IRemotingClient client)
    {
        _tcpClient = new RemotingTcpClient(client.Config.ServerHostName, client.Config.ServerPort, this);
        if (client.MessageEncryption)
            throw new NotImplementedException("MessageEncryption not implemented");

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
            
        if (_tcpClient.IsConnected)
            return;
        
        LastException = null;

        _tcpClient.ConnectAsync();
    }

    /// <summary>
    /// Handle error notification
    /// </summary>
    /// <param name="error">Socket error code</param>
    public void OnError(SocketError error)
    {
        LastException = new NetworkException(error.ToString());
        ErrorOccured?.Invoke("TcpClientError", LastException);
    }

    /// <summary>
    /// Event procedure: Called when a message from server is received.
    /// </summary>
    /// <param name="data">Received data</param>
    public void OnMessage(byte[] data)
    {
        ReceiveMessage?.Invoke(data);
    }

    /// <summary>
    /// Closes the connection.
    /// </summary>
    public void Disconnect()
    {
        if (_tcpClient == null)
            return;

        if (_tcpClient.IsConnected)
        {
            try
            {
                _tcpClient.DisconnectAsync();
            }
            catch 
            {
            }
        }

        _tcpClient.Dispose();
        _tcpClient = null;
    }

    /// <summary>
    /// Gets whether the connection is established or not.
    /// </summary>
    public bool IsConnected => _tcpClient?.IsConnected ?? false;

    /// <summary>
    /// Gets the raw message transport component for this connection.
    /// </summary>
    public IRawMessageTransport RawMessageTransport => this;
    
    /// <summary>
    /// Sends a message to the server.
    /// </summary>
    /// <param name="rawMessage">Raw message data</param>
    public void SendMessage(byte[] rawMessage)
    {
        _tcpClient.SendAsync(rawMessage);
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
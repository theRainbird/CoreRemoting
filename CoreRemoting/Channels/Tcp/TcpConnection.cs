using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WatsonTcp;

namespace CoreRemoting.Channels.Tcp;

/// <summary>
/// TCP-Connection.
/// </summary>
public class TcpConnection : IRawMessageTransport
{
    private readonly ClientMetadata _clientMetadata;
    private readonly WatsonTcpServer _tcpServer;
    private readonly IRemotingServer _server;
    private RemotingSession _session;

    /// <summary>
    /// Craetes a new TCPConnection instance.
    /// </summary>
    /// <param name="clientMetadata">Client info</param>
    /// <param name="tcpServer">TCP server obejct</param>
    /// <param name="server">Remoting server instance</param>
    public TcpConnection(ClientMetadata clientMetadata, WatsonTcpServer tcpServer, IRemotingServer server)
    {
        _clientMetadata = clientMetadata ?? throw new ArgumentNullException(nameof(clientMetadata));
        _tcpServer = tcpServer ?? throw new ArgumentNullException(nameof(tcpServer));
        _server = server ?? throw new ArgumentException(nameof(server));
    }
    
    /// <summary>
    /// Event: Fires when a message is received from server.
    /// </summary>
    public event Action<byte[]> ReceiveMessage;

    /// <summary>
    /// Event: Fires when an error is occurred.
    /// </summary>
    public event Action<string, Exception> ErrorOccured;

    /// <summary>
    /// Fires the ReceiveMessage event.
    /// </summary>
    /// <param name="message">Fehlermeldung</param>
    /// <param name="ex">Ausnahme</param>
    internal void FireErrorOccured(string message, Exception ex)
    {
        ErrorOccured?.Invoke(message, ex);
    }

    /// <summary>
    /// Gets or sets the last exception.
    /// </summary>
    public NetworkException LastException { get; set; }

    /// <summary>
    /// Fires the ReceiveMessage event.
    /// </summary>
    /// <param name="rawMessage">Message</param>
    /// <param name="metadata">Metadata</param>
    internal void FireReceiveMessage(byte[] rawMessage, Dictionary<string, object> metadata)
    {
        if (!CreateSessionAsNeeded(metadata))
        {
            ReceiveMessage?.Invoke(rawMessage);
        }
    }

    /// <summary>
    /// Creates the <see cref="RemotingSession"/> if it's not yet created.
    /// </summary>
    private bool CreateSessionAsNeeded(Dictionary<string, object> metadata)
    {
        if (_session != null)
            return false;

        byte[] clientPublicKey = null;

        if (metadata != null)
        {
            var messageEncryption = ((System.Text.Json.JsonElement)metadata["MessageEncryption"]).GetBoolean();

            if (messageEncryption)
            {
                var shakeHands = ((System.Text.Json.JsonElement)metadata["ShakeHands"]).GetString();

                if (shakeHands != null)
                {
                    clientPublicKey =
                        Convert.FromBase64String(shakeHands);
                }
            }
        }

        _session =
            _server.SessionRepository.CreateSession(
                clientPublicKey,
                _clientMetadata.IpPort,
                _server,
                this);

        _session.BeforeDispose += BeforeDisposeSession;
        return true;
    }

    /// <summary>
    /// Closes the internal websocket session.
    /// </summary>
    private void BeforeDisposeSession()
    {
        _session = null;
        _tcpServer.DisconnectClientAsync(_clientMetadata.Guid, MessageStatus.Shutdown);
    }

    /// <summary>
    /// Sends a message to the server.
    /// </summary>
    /// <param name="rawMessage">Raw message data</param>
    public async Task<bool> SendMessageAsync(byte[] rawMessage) =>
        await _tcpServer.SendAsync(_clientMetadata.Guid, rawMessage)
            .ConfigureAwait(false);
}
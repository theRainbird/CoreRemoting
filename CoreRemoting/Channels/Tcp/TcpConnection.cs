using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CoreRemoting.Channels.Common;
using CoreRemoting.RpcMessaging;
using CoreRemoting.Toolbox;
using WatsonTcp;

namespace CoreRemoting.Channels.Tcp;

/// <summary>
/// TCP-Connection.
/// </summary>
public class TcpConnection : MessageTransportBase
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
    /// Fires the ErrorOccured event.
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="ex">Exception</param>
    internal void FireErrorOccured(string message, Exception ex) =>
        OnErrorOccured(message, ex);

    /// <summary>
    /// Fires the Disconnected event.
    /// </summary>
    internal void FireDisconnected() =>
        OnDisconnected();

    /// <summary>
    /// Fires the ReceiveMessage event.
    /// </summary>
    /// <param name="rawMessage">Message</param>
    /// <param name="metadata">Metadata</param>
    internal void FireReceiveMessage(byte[] rawMessage, Dictionary<string, object> metadata)
    {
        if (!CreateSessionAsNeeded(metadata))
        {
            OnReceiveMessage(rawMessage);
        }
    }

    /// <summary>
    /// Creates the <see cref="RemotingSession"/> if it's not yet created.
    /// </summary>
    private bool CreateSessionAsNeeded(Dictionary<string, object> metadata)
    {
        if (_session != null)
            return false;

        var handshake = new ClientHandshakeMessage
        {
            Metadata = metadata?.ToDictionary(kv => kv.Key, kv => $"{kv.Value}"),
            ClientAddress = _clientMetadata.IpPort,
        };

        _session = _server.SessionRepository
            .ResumeOrCreateSession(handshake, _server, this)
                .GetAwaiter().GetResult();

        _session.BeforeDispose += BeforeDisposeSession;
        return true;
    }

    /// <summary>
    /// Closes the internal tcp channel session.
    /// </summary>
    /// <remarks>
    /// This handler is subscribed to <see cref="RemotingSession.BeforeDispose"/> and is therefore invoked
    /// synchronously as part of the multicast delegate. WatsonTcp's <c>DisconnectClientAsync</c> can block
    /// for several seconds (it waits for the underlying socket to shut down). To avoid holding up the
    /// <see cref="RemotingSession.BeforeDispose"/> event - and thus the disposing thread and any other
    /// subscribers - the disconnect is offloaded to a fire-and-forget background task.
    /// </remarks>
    private void BeforeDisposeSession()
    {
        _session = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await _tcpServer.DisconnectClientAsync(_clientMetadata.Guid, MessageStatus.Shutdown)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Ignored: server may already be stopped
            }
        });
    }

    /// <summary>
    /// Sends a message to the server.
    /// </summary>
    /// <param name="rawMessage">Raw message data</param>
    /// <remarks>
    /// Sends directly through the WatsonTcp server and returns its result unchanged.
    /// The base send template is intentionally bypassed to preserve the original
    /// behavior of propagating transport exceptions to the caller.
    /// </remarks>
    public override async Task<bool> SendMessageAsync(byte[] rawMessage) =>
        await _tcpServer.SendAsync(_clientMetadata.Guid, rawMessage)
            .ConfigureAwait(false);

    /// <inheritdoc />
    protected override Task<bool> SendCoreAsync(byte[] rawMessage) =>
        throw new NotSupportedException(
            "TcpConnection sends directly via SendMessageAsync; the send template is not used.");
}

using System;
using System.Linq;
using System.Net;
using CoreRemoting.Channels.Null;

namespace CoreRemoting.Channels.Websocket;

/// <summary>
/// Websocket connection.
/// </summary>
public class NullServerConnection : NullTransport, IAsyncDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NullServerConnection"/> class.
    /// </summary>
    public NullServerConnection(NullMessage connectionMessage, IRemotingServer remotingServer)
    {
        ConnectionMessage = connectionMessage ?? throw new ArgumentNullException(nameof(connectionMessage));
        ClientAddress = IPAddress.Loopback.ToString(); // connections are always local
        RemotingServer = remotingServer; // note: server is not required, null is acceptable for the unit tests
        ThisEndpoint = connectionMessage.Receiver ?? throw new ArgumentNullException(nameof(connectionMessage.Receiver));
        RemoteEndpoint = connectionMessage.Sender ?? throw new ArgumentNullException(nameof(connectionMessage.Sender));
    }

    private string ClientAddress { get; set; }

    private NullMessage ConnectionMessage { get; set; }

    private IRemotingServer RemotingServer { get; set; }

    private RemotingSession Session { get; set; }

    /// <summary>
    /// Starts listening to the incoming messages.
    /// </summary>
    public override Guid StartListening()
    {
        var sessionId = CreateRemotingSession();
        base.StartListening();
        return sessionId;
    }

    /// <summary>
    /// Creates <see cref="RemotingSession"/> for the incoming websocket connection.
    /// </summary>
    private Guid CreateRemotingSession()
    {
        byte[] clientPublicKey = null;

        // get encryption metadata from NullMessage
        if (ConnectionMessage.Metadata != null &&
            ConnectionMessage.Metadata.Length == 2 &&
            ConnectionMessage.Metadata.First() == nameof(RemotingClient.PublicKey))
        {
            clientPublicKey = Convert.FromBase64String(
                ConnectionMessage.Metadata.Last());
        }

        if (RemotingServer != null)
        {
            Session = RemotingServer.SessionRepository.CreateSession(
                clientPublicKey, ClientAddress, RemotingServer, this);

            return Session.SessionId;
        }

        return Guid.NewGuid();
    }
}

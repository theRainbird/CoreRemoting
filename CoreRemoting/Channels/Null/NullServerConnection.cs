using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
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
    public override async Task<Guid> StartListening()
    {
        var sessionId = await CreateRemotingSession().ConfigureAwait(false);
        await base.StartListening().ConfigureAwait(false);
        return sessionId;
    }

    /// <summary>
    /// Creates <see cref="RemotingSession"/> for the incoming websocket connection.
    /// </summary>
    private async Task<Guid> CreateRemotingSession()
    {
        var messageEncryption = false;
        byte[] clientPublicKey = null;

        // get encryption metadata from NullMessage
        if (ConnectionMessage.Metadata != null)
        {
            var md = ConnectionMessage.Metadata;
            if (md.TryGetValue(nameof(RemotingClient.MessageEncryption), out var me))
            {
                messageEncryption = Convert.ToBoolean(me);
            }

            if (md.TryGetValue(nameof(RemotingClient.PublicKey), out var pk))
            {
                clientPublicKey = Convert.FromBase64String(pk);
            }
        }

        if (RemotingServer != null)
        {
            // note: null channel sessions are not resumable
            Session = await RemotingServer.SessionRepository.CreateSession(
                messageEncryption, clientPublicKey, ClientAddress, RemotingServer, this)
                    .ConfigureAwait(false);

            return Session.SessionId;
        }

        return Guid.NewGuid();
    }
}

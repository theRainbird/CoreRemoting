using System;
using System.Threading.Tasks;
using CoreRemoting.RpcMessaging;
using CoreRemoting.Toolbox;

namespace CoreRemoting.Channels.Common;

/// <summary>
/// Base class for server side connections that own a <see cref="RemotingSession"/>
/// created or resumed from the client's handshake message.
/// </summary>
public abstract class ServerConnectionBase : ReadLoopTransportBase
{
    /// <summary>
    /// The remoting server this connection belongs to.
    /// </summary>
    protected IRemotingServer RemotingServer { get; set; }

    /// <summary>
    /// The remoting session bound to this connection.
    /// </summary>
    protected RemotingSession Session { get; set; }

    /// <summary>
    /// Creates a new remoting session for this connection or resumes an existing
    /// parked session, if the client presented a resumable session ID whose
    /// public key matches, and subscribes the <see cref="BeforeDisposeSession"/>
    /// handler.
    /// </summary>
    /// <param name="handshake">Client handshake message</param>
    /// <returns>The session ID</returns>
    protected async Task<Guid> CreateRemotingSessionAsync(ClientHandshakeMessage handshake)
    {
        Session = await RemotingServer.SessionRepository
            .ResumeOrCreateSession(handshake, RemotingServer, this)
                .ConfigureAwait(false);

        Session.BeforeDispose += BeforeDisposeSession;

        return Session.SessionId;
    }

    /// <summary>
    /// Called before the bound session is disposed.
    /// </summary>
    protected virtual void BeforeDisposeSession()
    {
        Session = null;
    }
}

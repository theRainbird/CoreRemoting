using System;
using System.Linq;
using System.Net.Sockets;
using NetCoreServer;

namespace CoreRemoting.Channels.TcpNetCoreServer;

/// <summary>
/// TCP session is used to read and write data from the connected TCP client
/// </summary>
/// <remarks>Thread-safe</remarks>
class RemotingTcpSession : TcpSession, IRawMessageTransport
{
    private RemotingSession _session;
    private readonly IRemotingServer _server;

    /// <summary>
    /// Craetes a new RemotingTcpSession instance.
    /// </summary>
    /// <param name="tcpServer">TCP server obejct</param>
    /// <param name="server">Remoting server instance</param>
    public RemotingTcpSession(TcpServer tcpServer, IRemotingServer server) : base(tcpServer)
    {
        _server = server;
    }

    protected override void OnConnected()
    {
        if (_session == null)
        {
            byte[] clientPublicKey = null;

            // if (metadata != null)
            // {
            //     var messageEncryption = ((System.Text.Json.JsonElement)metadata["MessageEncryption"]).GetBoolean();
            //
            //     if (messageEncryption)
            //     {
            //         var shakeHands = ((System.Text.Json.JsonElement)metadata["ShakeHands"]).GetString();
            //
            //         if (shakeHands != null)
            //         {
            //             clientPublicKey =
            //                 Convert.FromBase64String(shakeHands);
            //         }
            //     }
            // }

            _session =
                _server.SessionRepository.CreateSession(
                    clientPublicKey,
                    _server,
                    this);

            _session.BeforeDispose += BeforeDisposeSession;
        }
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
        ReceiveMessage?.Invoke(buffer.Skip((int)offset).Take((int)size).ToArray());
    }
    
    /// <summary>
    /// Closes the internal websocket session.
    /// </summary>
    private void BeforeDisposeSession()
    {
        _session = null;
        Disconnect();
    }

    protected override void OnError(SocketError error)
    {
        LastException = new NetworkException(error.ToString());
        ErrorOccured?.Invoke("TcpServerError", LastException);
    }

    public event Action<byte[]> ReceiveMessage;
    public event Action<string, Exception> ErrorOccured;
    public NetworkException LastException { get; set; }
    public void SendMessage(byte[] rawMessage)
    {
        SendAsync(rawMessage);
    }
}

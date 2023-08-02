using System;
using System.Net;
using NetCoreServer;

namespace CoreRemoting.Channels.TcpNetCoreServer;

class RemotingTcpServer : TcpServer
{
    private readonly IRemotingServer _remotingServer;

    public RemotingTcpServer(IPAddress address, int port, IRemotingServer remotingServer) : base(address, port)
    {
        _remotingServer = remotingServer ?? throw new ArgumentNullException(nameof(remotingServer));
    }

    protected override TcpSession CreateSession() { return new RemotingTcpSession(this, _remotingServer); }
}

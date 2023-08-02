using System.Linq;
using System.Net;
using System.Net.Sockets;
using TcpClient = NetCoreServer.TcpClient;

namespace CoreRemoting.Channels.TcpNetCoreServer;

class RemotingTcpClient : TcpClient
{
    private readonly TcpNetCoreClientChannel _tcpNetCoreClientChannel;

    public RemotingTcpClient(string address, int port, TcpNetCoreClientChannel tcpNetCoreClientChannel) : base(
        Dns.GetHostAddresses(address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork), port)
    {
        _tcpNetCoreClientChannel = tcpNetCoreClientChannel;
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
        _tcpNetCoreClientChannel.OnMessage(buffer.Skip((int)offset).Take((int)size).ToArray());
    }

    protected override void OnError(SocketError error)
    {
        _tcpNetCoreClientChannel.OnError(error);
    }
}

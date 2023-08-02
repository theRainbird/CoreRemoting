using System;
using System.Net;
using TcpServer = NetCoreServer.TcpServer;

namespace CoreRemoting.Channels.TcpNetCoreServer;

/// <summary>
/// Server side TCP channel implementation.
/// </summary>
public class TcpNetCoreServerChannel : IServerChannel
{
    private IRemotingServer _remotingServer;
    private TcpServer _tcpServer;
    
    /// <summary>
    /// Initializes the channel.
    /// </summary>
    /// <param name="server">CoreRemoting sever</param>
    public void Init(IRemotingServer server)
    {
        _remotingServer = server ?? throw new ArgumentNullException(nameof(server));
        _tcpServer = new RemotingTcpServer(IPAddress.Any, _remotingServer.Config.NetworkPort, _remotingServer);
    }

    /// <summary>
    /// Start listening for client requests.
    /// </summary>
    public void StartListening()
    {
        if (_tcpServer == null)
            throw new InvalidOperationException("Channel is not initialized.");
        
        _tcpServer.Start();
    }

    /// <summary>
    /// Stop listening for client requests.
    /// </summary>
    public void StopListening()
    {
        if (_tcpServer == null)
            return;
        
        if (_tcpServer.IsStarted)
            _tcpServer.Stop();
    }

    /// <summary>
    /// Gets whether the channel is listening or not.
    /// </summary>
    public bool IsListening => 
        _tcpServer?.IsStarted ?? false;
    
    /// <summary>
    /// Stops listening and frees managed resources.
    /// </summary>
    public void Dispose()
    {
        if (_tcpServer != null)
        {
            _tcpServer.Stop();
            _tcpServer.Dispose();
            _tcpServer = null;
        }
    }
}
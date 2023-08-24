using System;
using System.Collections.Concurrent;
using WatsonTcp;

namespace CoreRemoting.Channels.Tcp;

/// <summary>
/// Server side TCP channel implementation.
/// </summary>
public class TcpServerChannel : IServerChannel
{
    private IRemotingServer _remotingServer;
    private WatsonTcpServer _tcpServer;
    private readonly ConcurrentDictionary<Guid, TcpConnection> _connections;

    /// <summary>
    /// Creates a new instance of the TcpServerChannel class.
    /// </summary>
    public TcpServerChannel()
    {
        _connections = new ConcurrentDictionary<Guid, TcpConnection>();
    }
    
    /// <summary>
    /// Initializes the channel.
    /// </summary>
    /// <param name="server">CoreRemoting sever</param>
    public void Init(IRemotingServer server)
    {
        _remotingServer = server ?? throw new ArgumentNullException(nameof(server));

        _tcpServer = new WatsonTcpServer(null, _remotingServer.Config.NetworkPort);
        _tcpServer.Settings.NoDelay = true;
        _tcpServer.Events.ClientConnected += OnClientConnected;
        _tcpServer.Events.ClientDisconnected += OnClientDisconnected;
        _tcpServer.Events.MessageReceived += OnTcpMessageReceived;
    }

    private void OnClientConnected(object sender, ConnectionEventArgs e)
    {
        _connections.TryAdd(e.Client.Guid, new TcpConnection(e.Client, _tcpServer, _remotingServer));
    }
    
    private void OnClientDisconnected(object sender, DisconnectionEventArgs e)
    {
        _connections.TryRemove(e.Client.Guid, out _);
    }

    private void OnTcpMessageReceived(object sender, MessageReceivedEventArgs e)
    {
        if (_connections.TryGetValue(e.Client.Guid, out TcpConnection connection))
        {
            connection.FireReceiveMessage(e.Data, e.Metadata);   
        }
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
        
        _tcpServer.Stop();
    }

    /// <summary>
    /// Gets whether the channel is listening or not.
    /// </summary>
    public bool IsListening => 
        _tcpServer?.IsListening ?? false;
    
    /// <summary>
    /// Stops listening and frees managed resources.
    /// </summary>
    public void Dispose()
    {
        if (_tcpServer != null)
        {
            _tcpServer.Dispose();
            _tcpServer = null;
        }
    }
}

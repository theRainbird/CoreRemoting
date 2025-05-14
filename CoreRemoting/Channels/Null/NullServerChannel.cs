using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Castle.MicroKernel.SubSystems.Conversion;
using CoreRemoting.Channels.Websocket;
using static CoreRemoting.Channels.Null.NullMessageQueue;

namespace CoreRemoting.Channels.Null;

/// <summary>
/// Simple in-process channel, server-side.
/// </summary>
public class NullServerChannel : IServerChannel
{
    /// <inheritdoc/>
    public void Init(IRemotingServer server)
    {
        Server = server;
        SetUrl(Server.Config.HostName, Server.Config.NetworkPort);
    }

    /// <summary>
    /// Sets the server URL.
    /// </summary>
    /// <param name="host">Server host</param>
    /// <param name="port">Server port</param>
    internal void SetUrl(string host, int port) =>
        Url = $"null://{host}:{port}/rpc";

    /// <summary>
    /// Gets the associated remoting server.
    /// </summary>
    public IRemotingServer Server { get; private set; }

    internal ConcurrentDictionary<Guid, NullServerConnection> Connections { get; } = new();

    /// <inheritdoc/>
    public string Url { get; private set; }

    /// <inheritdoc/>
    public bool IsListening { get; private set; }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        StopListening();

        foreach (var conn in Connections)
        {
            await conn.Value.DisconnectAsync();
        }
    }

    /// <inheritdoc/>
    public void StartListening()
    {
        IsListening = true;
        StartListener(Url);

        _ = Task.Factory.StartNew(async () =>
        {
            while (IsListening)
                await ReceiveConnections();
        });
    }

    /// <inheritdoc/>
    public void StopListening()
    {
        if (IsListening)
        {
            IsListening = false;
            StopListener(Url);
        }
    }

    private async Task ReceiveConnections()
    {
        await foreach (var msg in
            ReceiveMessagesAsync(Url, string.Empty, Url)
                .ConfigureAwait(false))
        {
            var connection = new NullServerConnection(msg, Server);
            var sessionId = connection.StartListening();
            Connections[sessionId] = connection;
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
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

    private CancellationTokenSource _acceptCts;

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

        _acceptCts = new CancellationTokenSource();
        var token = _acceptCts.Token;

        _ = Task.Factory.StartNew(async () =>
        {
            while (!token.IsCancellationRequested && IsListening)
                await ReceiveConnections(token);
        }, token);
    }

    /// <inheritdoc/>
    public void StopListening()
    {
        if (IsListening)
        {
            IsListening = false;
            StopListener(Url);
        }

        _acceptCts?.Cancel();
        _acceptCts?.Dispose();
        _acceptCts = null;
    }

    private async Task ReceiveConnections(CancellationToken token)
    {
        await foreach (var msg in
            ReceiveMessagesAsync(Url, string.Empty, Url, token)
                .ConfigureAwait(false))
        {
            var connection = new NullServerConnection(msg, Server);
            var sessionId = await connection.StartListening().ConfigureAwait(false);
            Connections[sessionId] = connection;
        }
    }
}

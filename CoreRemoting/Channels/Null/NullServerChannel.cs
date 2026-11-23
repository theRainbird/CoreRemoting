using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Channels.Common;
using static CoreRemoting.Channels.Null.NullMessageQueue;

namespace CoreRemoting.Channels.Null;

/// <summary>
/// Simple in-process channel, server-side.
/// </summary>
public class NullServerChannel : ServerChannelBase
{
    /// <inheritdoc/>
    public override void Init(IRemotingServer server)
    {
        base.Init(server);
        SetUrl(Server.Config.HostName, Server.Config.NetworkPort);
    }

    /// <summary>
    /// Sets the server URL.
    /// </summary>
    /// <param name="host">Server host</param>
    /// <param name="port">Server port</param>
    internal void SetUrl(string host, int port) =>
        Url = $"null://{host}:{port}/rpc";

    internal ConcurrentDictionary<Guid, NullServerConnection> Connections { get; } = new();

    private CancellationTokenSource _acceptCts;

    /// <inheritdoc/>
    public string Url { get; private set; }

    private bool _isListening;

    /// <inheritdoc/>
    public override bool IsListening => _isListening;

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        StopListening();

        foreach (var conn in Connections)
        {
            await conn.Value.DisconnectAsync();
        }
    }

    /// <inheritdoc/>
    public override void StartListening()
    {
        _isListening = true;
        StartListener(Url);

        _acceptCts = new CancellationTokenSource();
        var token = _acceptCts.Token;

        _ = Task.Factory.StartNew(async () =>
        {
            while (!token.IsCancellationRequested && IsListening)
                await ReceiveConnections(token);
        },
        token);
    }

    /// <inheritdoc/>
    public override void StopListening()
    {
        if (IsListening)
        {
            _isListening = false;
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

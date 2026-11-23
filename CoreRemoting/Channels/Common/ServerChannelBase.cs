using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using CoreRemoting.Toolbox;

namespace CoreRemoting.Channels.Common;

/// <summary>
/// Base class for server side channels.
/// </summary>
public abstract class ServerChannelBase : IServerChannel
{
    /// <summary>
    /// The remoting server hosted by this channel.
    /// </summary>
    public IRemotingServer Server { get; protected set; }

    /// <inheritdoc />
    public virtual void Init(IRemotingServer server)
    {
        Server = server ?? throw new ArgumentNullException(nameof(server));
    }

    /// <inheritdoc />
    public abstract bool IsListening { get; }

    /// <inheritdoc />
    public abstract void StartListening();

    /// <inheritdoc />
    public abstract void StopListening();

    /// <inheritdoc />
    public abstract ValueTask DisposeAsync();

    /// <summary>
    /// Registers a connection in the given registry and removes plus disposes it
    /// automatically as soon as it disconnects.
    /// </summary>
    /// <param name="connections">Connection registry</param>
    /// <param name="sessionId">Session identity of the connection</param>
    /// <param name="connection">The connection to register</param>
    /// <typeparam name="T">Connection type</typeparam>
    protected static void RegisterConnection<T>(
        ConcurrentDictionary<Guid, T> connections, Guid sessionId, T connection)
        where T : MessageTransportBase, IAsyncDisposable
    {
        connections[sessionId] = connection;

        // don't await DisposeAsync from the loop's finally, that would deadlock;
        // dispose off the loop's thread, after it has fully completed
        connection.Disconnected += () =>
        {
            if (connections.TryRemove(sessionId, out var conn))
            {
                _ = Task.Run(async () =>
                    // async void event handler should never throw
                    await conn.TryDisposeAsync()
                        .ConfigureAwait(false));
            }
        };
    }

    /// <summary>
    /// Disposes all given connections, ignoring disposal errors.
    /// </summary>
    /// <param name="connections">Connections to dispose</param>
    protected static async Task DisposeConnectionsAsync(IEnumerable<IAsyncDisposable> connections)
    {
        foreach (var conn in connections)
            await conn.TryDisposeAsync()
                .ConfigureAwait(false);
    }
}

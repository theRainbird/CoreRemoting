using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Channels.Common;
using static CoreRemoting.Channels.Null.NullMessageQueue;

namespace CoreRemoting.Channels.Null;

/// <summary>
/// Simple in-process channel, base transport class.
/// </summary>
public class NullTransport : ReadLoopTransportBase
{
    /// <summary>
    /// This endpoint.
    /// </summary>
    public string ThisEndpoint { get; protected set; }

    /// <summary>
    /// Remote endpoint.
    /// </summary>
    public string RemoteEndpoint { get; protected set; }

    /// <summary>
    /// The read loop is the single fire point of the
    /// <see cref="MessageTransportBase.Disconnected"/> event.
    /// </summary>
    protected override void OnLoopStarting() =>
        IsConnectedFlag = true;

    /// <summary>
    /// Disconnects from the remote endpoint.
    /// </summary>
    /// <remarks>
    /// Not thread-safe: concurrent calls may each send a stop signal to the peer.
    /// Serialized externally by <c>RemotingClient</c>.
    /// </remarks>
    public override async Task DisconnectAsync()
    {
        var wasConnected = IsConnected;
        IsConnectedFlag = false;

        CancelReadLoop();
        await DetachReadLoopAsync()
            .ConfigureAwait(false);

        if (wasConnected)
            SendMessage(ThisEndpoint, RemoteEndpoint, null, []);
    }

    /// <summary>
    /// Reads incoming messages from the in-process queue and fires the
    /// <see cref="MessageTransportBase.ReceiveMessage"/> event.
    /// </summary>
    /// <param name="ct">Cancellation token of the read loop</param>
    protected override async Task ReceiveLoopBodyAsync(CancellationToken ct)
    {
        while (IsConnected && !ct.IsCancellationRequested)
        {
            await foreach (var message in
                ReceiveMessagesAsync(null, RemoteEndpoint, ThisEndpoint, ct)
                    .ConfigureAwait(false))
            {
                if (ct.IsCancellationRequested)
                    break;

                OnReceiveMessage(message.Message ?? []);

                if (message.IsStopSignal)
                {
                    IsConnectedFlag = false;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Not used: the null transport pumps its messages through the in-process
    /// message queue in <see cref="ReceiveLoopBodyAsync"/>.
    /// </summary>
    /// <param name="ct">Cancellation token of the read loop</param>
    protected override Task<byte[]> ReadNextAsync(CancellationToken ct) =>
        Task.FromResult<byte[]>(null);

    /// <inheritdoc />
    protected override Task<bool> SendCoreAsync(byte[] rawMessage)
    {
        SendMessage(ThisEndpoint, RemoteEndpoint, rawMessage);
        return Task.FromResult(true);
    }
}

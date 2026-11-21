using System;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Threading;
using static CoreRemoting.Channels.Null.NullMessageQueue;

namespace CoreRemoting.Channels.Null;

/// <summary>
/// Simple in-process channel, base transport class.
/// </summary>
public class NullTransport : IRawMessageTransport, IAsyncDisposable
{
    /// <summary>
    /// Buffer size to read incoming messages.
    /// Note: LOH threshold is ~85 kilobytes
    /// </summary>
    protected const int BufferSize = 16 * 1024;

    /// <summary>
    /// This endpoint.
    /// </summary>
    public string ThisEndpoint { get; protected set; }

    /// <summary>
    /// Remote endpoint.
    /// </summary>
    public string RemoteEndpoint { get; protected set; }

    /// <inheritdoc />
    public bool IsConnected { get; protected set; }

    /// <inheritdoc />
    public NetworkException LastException { get; set; }

    /// <summary>
    /// Event: fires when the channel is connected.
    /// </summary>
    public event Action Connected;

    private Task _readLoop;
    private CancellationTokenSource _readCts;
    private AtomicBool _disconnectFired;

    /// <summary>
    /// Fires the <see cref="Connected"/> event.
    /// </summary>
    protected void OnConnected() =>
        Connected?.Invoke();

    /// <inheritdoc />
    public event Action Disconnected;

    /// <summary>
    /// Fires the <see cref="Disconnected"/> event.
    /// </summary>
    protected void OnDisconnected() =>
        Disconnected?.Invoke();

    /// <inheritdoc />
    public event Action<byte[]> ReceiveMessage;

    /// <summary>
    /// Fires the <see cref="ReceiveMessage"/> event.
    /// </summary>
    protected void OnReceiveMessage(byte[] message) =>
        ReceiveMessage?.Invoke(message);

    /// <inheritdoc />
    public event Action<string, Exception> ErrorOccured;

    /// <summary>
    /// Fires the <see cref="ErrorOccured"/> event.
    /// </summary>
    protected void OnErrorOccured(string message, Exception ex) =>
        ErrorOccured?.Invoke(message, ex);

    /// <summary>
    /// Starts listening for incoming messages.
    /// A running read loop from a previous connection is cancelled and awaited first.
    /// </summary>
    public virtual async Task<Guid> StartListening()
    {
        using var oldCts = Interlocked.Exchange(ref _readCts, new CancellationTokenSource());
        oldCts?.Cancel();

        await DetachReadLoopAsync()
            .ConfigureAwait(false);

        _disconnectFired.Reset();
        IsConnected = true;
        _readLoop = ReadIncomingMessages(_readCts.Token);

        return Guid.Empty;
    }

    /// <summary>
    /// Disconnects from the remote endpoint.
    /// </summary>
    /// <remarks>
    /// Not thread-safe: concurrent calls may each send a stop signal to the peer.
    /// Serialized externally by <c>RemotingClient</c>.
    /// </remarks>
    public virtual async Task DisconnectAsync()
    {
        var wasConnected = IsConnected;
        IsConnected = false;

        _readCts?.Cancel();
        await DetachReadLoopAsync()
            .ConfigureAwait(false);

        if (wasConnected)
            SendMessage(ThisEndpoint, RemoteEndpoint, null, []);
    }

    /// <inheritdoc/>
    public virtual async ValueTask DisposeAsync()
    {
        await DisconnectAsync()
            .ConfigureAwait(false);

        using var cts = Interlocked.Exchange(ref _readCts, null);
        cts?.Cancel();
    }

    /// <summary>
    /// Atomically detaches the current read loop and awaits its completion.
    /// </summary>
    private async Task DetachReadLoopAsync()
    {
        var loop = _readLoop;
        _readLoop = null;

        if (loop != null)
            await loop.ConfigureAwait(false);
    }

    /// <summary>
    /// Reads incoming messages and fires the <see cref="ReceiveMessage"/> event.
    /// </summary>
    private async Task ReadIncomingMessages(CancellationToken ct)
    {
        try
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
                        IsConnected = false;
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // expected on explicit disconnect
        }
        catch (Exception ex)
        {
            LastException = ex as NetworkException ??
                new NetworkException(ex.Message, ex);

            OnErrorOccured(ex.Message, LastException);
        }
        finally
        {
            IsConnected = false;

            // the loop is the single fire point, the flag guards future regressions
            if (_disconnectFired.TrySet())
                OnDisconnected();
        }
    }

    /// <inheritdoc />
    public Task<bool> SendMessageAsync(byte[] rawMessage)
    {
        try
        {
            SendMessage(ThisEndpoint, RemoteEndpoint, rawMessage);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            LastException = ex as NetworkException ??
                new NetworkException(ex.Message, ex);

            OnErrorOccured(ex.Message, LastException);
            return Task.FromResult(false);
        }
    }
}
using System;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Threading;
using CoreRemoting.Toolbox;

namespace CoreRemoting.Channels.Common;

/// <summary>
/// Base class for transports that pump incoming messages with a dedicated read loop.
/// Provides the full loop lifecycle: start, cancel, detach and the single-fire
/// disconnect semantics guarded by a read loop instance counter.
/// </summary>
public abstract class ReadLoopTransportBase : MessageTransportBase, IAsyncDisposable
{
    /// <summary>
    /// Buffer size to read incoming messages.
    /// Note: LOH threshold is ~85 kilobytes
    /// </summary>
    protected const int BufferSize = 16 * 1024;

    private Task _readLoop;
    private CancellationTokenSource _readLoopCts;
    private int _readLoopInstance;

    /// <summary>
    /// Connection state flag maintained by the read loop lifecycle.
    /// </summary>
    protected bool IsConnectedFlag;

    /// <summary>
    /// Gets a value indicating whether the transport is connected.
    /// Derived classes may add live connection checks on top of the flag.
    /// </summary>
    public virtual bool IsConnected => IsConnectedFlag;

    /// <summary>
    /// Lock that serializes receive operations on the underlying connection.
    /// </summary>
    protected AsyncLock ReceiveLock { get; } = new();

    /// <summary>
    /// Lock that serializes send operations on the underlying connection.
    /// </summary>
    protected AsyncLock SendLock { get; } = new();

    /// <summary>
    /// Starts listening for incoming messages.
    /// A running read loop from a previous connection is cancelled and awaited first.
    /// </summary>
    /// <returns>
    /// Client session identity, if used on server.
    /// </returns>
    public virtual async Task<Guid> StartListening()
    {
        // increment instance before detaching so the old loop knows it's obsolete
        var instance = Interlocked.Increment(ref _readLoopInstance);

        using var cts = Interlocked.Exchange(ref _readLoopCts, new CancellationTokenSource());
        cts?.Cancel();

        await DetachReadLoopAsync()
            .ConfigureAwait(false);

        OnLoopStarting();

        _readLoop = RunReadLoopAsync(instance, _readLoopCts.Token);

        return Guid.Empty;
    }

    /// <summary>
    /// Hook invoked after the previous loop has been detached and before
    /// the new read loop is started.
    /// </summary>
    protected virtual void OnLoopStarting()
    {
    }

    /// <summary>
    /// Disconnects from the remote endpoint: cancels the read loop and awaits
    /// its completion. The loop itself is the single fire point of the
    /// <see cref="MessageTransportBase.Disconnected"/> event.
    /// </summary>
    public virtual async Task DisconnectAsync()
    {
        CancelReadLoop();

        await DetachReadLoopAsync()
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask DisposeAsync()
    {
        await DisconnectAsync()
            .ConfigureAwait(false);

        using var cts = Interlocked.Exchange(ref _readLoopCts, null);
        cts?.Cancel();

        ReceiveLock.TryDispose();
        SendLock.TryDispose();
    }

    /// <summary>
    /// Cancels the cancellation token source of the current read loop.
    /// </summary>
    protected void CancelReadLoop() =>
        _readLoopCts?.Cancel();

    /// <summary>
    /// Invalidates the current read loop so it will not fire the
    /// <see cref="MessageTransportBase.Disconnected"/> event anymore, cancels it
    /// and awaits its completion. Call before establishing a new connection, when
    /// a late teardown of the previous connection must not reach the client.
    /// </summary>
    protected async Task InvalidateReadLoopAsync()
    {
        Interlocked.Increment(ref _readLoopInstance);

        using var cts = Interlocked.Exchange(ref _readLoopCts, null);
        cts?.Cancel();

        await DetachReadLoopAsync()
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Determines whether the given loop instance is still the current one.
    /// </summary>
    /// <param name="instance">Loop instance identity</param>
    protected bool IsCurrentReadLoop(int instance) =>
        Volatile.Read(ref _readLoopInstance) == instance;

    /// <summary>
    /// Atomically detaches the current read loop and awaits its completion.
    /// </summary>
    protected async Task DetachReadLoopAsync()
    {
        var loop = _readLoop;
        _readLoop = null;

        if (loop != null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch
            {
                /* handled in loop */
            }
        }
    }

    private async Task RunReadLoopAsync(int instance, CancellationToken ct)
    {
        try
        {
            await ReceiveLoopBodyAsync(ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // expected on explicit disconnect
        }
        catch (Exception ex)
        {
            if (!IsNormalShutdown(ex))
                ReportReceiveError(ex);
        }
        finally
        {
            await OnReadLoopEndedAsync(instance)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Default receive loop body: reads messages until the transport stops,
    /// the stream ends or cancellation is requested.
    /// </summary>
    /// <param name="ct">Cancellation token of the read loop</param>
    protected virtual async Task ReceiveLoopBodyAsync(CancellationToken ct)
    {
        while (ShouldContinueReceiving(ct))
        {
            var message = await ReadNextAsync(ct)
                .ConfigureAwait(false);

            if (message == null)
                break;

            if (ShouldDeliverMessage(message))
                OnReceiveMessage(message);
        }
    }

    /// <summary>
    /// Determines whether the read loop continues pumping.
    /// </summary>
    /// <param name="ct">Cancellation token of the read loop</param>
    protected virtual bool ShouldContinueReceiving(CancellationToken ct) =>
        IsConnectedFlag && !ct.IsCancellationRequested;

    /// <summary>
    /// Reads the next message from the underlying connection.
    /// </summary>
    /// <param name="ct">Cancellation token of the read loop</param>
    /// <returns>The next message, or null to end the read loop</returns>
    protected abstract Task<byte[]> ReadNextAsync(CancellationToken ct);

    /// <summary>
    /// Determines whether a received message is delivered via the
    /// <see cref="MessageTransportBase.ReceiveMessage"/> event.
    /// </summary>
    /// <param name="message">The received message</param>
    protected virtual bool ShouldDeliverMessage(byte[] message) => true;

    /// <summary>
    /// Reports a failure of the receive loop. The default implementation wraps
    /// and fires the error like <see cref="MessageTransportBase.ReportError"/>.
    /// </summary>
    /// <param name="ex">The exception thrown by the receive loop</param>
    protected virtual void ReportReceiveError(Exception ex) =>
        ReportError(ex);

    /// <summary>
    /// Called when the read loop ended. The current loop is the single fire point
    /// of the <see cref="MessageTransportBase.Disconnected"/> event; a superseded
    /// loop must not touch the state of the current connection.
    /// </summary>
    /// <param name="instance">Loop instance identity</param>
    protected virtual Task OnReadLoopEndedAsync(int instance)
    {
        if (IsCurrentReadLoop(instance))
        {
            IsConnectedFlag = false;
            OnDisconnected();
        }

        return Task.CompletedTask;
    }
}

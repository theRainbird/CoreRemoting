using System;
using System.Net.WebSockets;
using CoreRemoting.IO;
using System.Threading.Tasks;
using System.Threading;
using CoreRemoting.Threading;

namespace CoreRemoting.Channels.Websocket;

/// <summary>
/// Abstract web socket transport for reading and writing messages.
/// </summary>
public abstract class WebsocketTransport : IRawMessageTransport, IAsyncDisposable
{
    /// <summary>
    /// Buffer size to read incoming messages.
    /// Note: LOH threshold is ~85 kilobytes
    /// </summary>
    protected const int BufferSize = 16 * 1024;

    /// <summary>
    /// Max time to wait for the peer to complete the close handshake on disconnect.
    /// After this timeout the read loop is cancelled, which may cause an RST.
    /// </summary>
    protected virtual int CloseHandshakeTimeoutMs => 1000;

    /// <inheritdoc />
    public NetworkException LastException { get; set; }

    /// <summary>
    /// Event: fires when the channel is connected.
    /// </summary>
    public event Action Connected;

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
    /// Fires the <see cref="ErrorOccured"/> event.
    /// </summary>
    protected void OnErrorOccured(string error, Exception ex) =>
        ErrorOccured?.Invoke(error, ex);

    /// <inheritdoc />
    public event Action<string, Exception> ErrorOccured;

    /// <summary>
    /// Web socket used to read and write messages.
    /// </summary>
    protected abstract WebSocket WebSocket { get; }

    private Task _readLoop;
    private CancellationTokenSource _readCts;
    private AtomicBool _disconnectFired;

    private AsyncLock ReceiveLock { get; } = new();

    private AsyncLock SendLock { get; } = new();

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
        _readLoop = ReadIncomingMessages(_readCts.Token);

        return Guid.Empty;
    }

    /// <summary>
    /// Completes the close handshake with the peer, then awaits the read loop.
    /// The loop itself closes the underlying socket in its finally block.
    /// </summary>
    public virtual async Task DisconnectAsync()
    {
        var detachTask = DetachReadLoopAsync();

        // give the loop a chance to complete the close handshake with the peer
        var completed = await Task.WhenAny(
            detachTask,
            Task.Delay(CloseHandshakeTimeoutMs)
        ).ConfigureAwait(false);

        if (completed != detachTask)
        {
            // peer did not complete the handshake in time, force cancel
            _readCts?.Cancel();
            await detachTask.ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public virtual async ValueTask DisposeAsync()
    {
        await DisconnectAsync()
            .ConfigureAwait(false);

        using var cts = Interlocked.Exchange(ref _readCts, null);
        cts?.Cancel();

        ReceiveLock.Dispose();
        SendLock.Dispose();
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
    /// Reads incoming websocket messages and fires the <see cref="ReceiveMessage"/> event.
    /// Responds to close frames to complete the close handshake.
    /// </summary>
    private async Task ReadIncomingMessages(CancellationToken ct)
    {
        var buffer = new byte[BufferSize];
        var segment = new ArraySegment<byte>(buffer);
        var webSocket = WebSocket;

        try
        {
            while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var ms = new SmallBlockMemoryStream();
                var closed = false;

                while (true)
                {
                    var result = default(WebSocketReceiveResult);

                    using (await ReceiveLock)
                        result = await webSocket.ReceiveAsync(segment, ct)
                            .ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        closed = true;
                        break;
                    }

                    ms.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage)
                        break;
                }

                if (closed)
                {
                    // peer sent a close frame, send ours back to complete the handshake
                    try
                    {
                        if (webSocket.State == WebSocketState.CloseReceived)
                            using (await SendLock)
                                await webSocket.CloseOutputAsync(
                                    WebSocketCloseStatus.NormalClosure, "",
                                    CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // peer may already be gone
                    }

                    break;
                }

                if (ct.IsCancellationRequested)
                    break;

                var message = Array.Empty<byte>();
                if (ms.Length > 0)
                {
                    message = new byte[(int)ms.Length];
                    ms.Position = 0;
                    ms.Read(message, 0, message.Length);
                }

                ReceiveMessage?.Invoke(message);
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

            ErrorOccured?.Invoke(ex.Message, LastException);
        }
        finally
        {
            try { webSocket?.Dispose(); } catch { /* already disposed */ }

            // the loop is the single fire point, the flag guards future regressions
            if (_disconnectFired.TrySet())
                OnDisconnected();
        }
    }

    /// <inheritdoc />
    public async Task<bool> SendMessageAsync(byte[] rawMessage)
    {
        try
        {
            var ws = WebSocket;
            if (ws.State != WebSocketState.Open)
            {
                LastException = new NetworkException($"WebSocket is {ws.State}, cannot send.");
                return false;
            }

            using (await SendLock)
                await ws.SendAsync(
                    new ArraySegment<byte>(rawMessage),
                    WebSocketMessageType.Binary, true, CancellationToken.None)
                        .ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            LastException = ex as NetworkException ??
                new NetworkException(ex.Message, ex);

            ErrorOccured?.Invoke(ex.Message, LastException);
            return false;
        }
    }
}

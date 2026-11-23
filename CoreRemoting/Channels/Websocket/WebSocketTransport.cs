using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Channels.Common;
using CoreRemoting.IO;
using CoreRemoting.Toolbox;

namespace CoreRemoting.Channels.Websocket;

/// <summary>
/// Abstract web socket transport for reading and writing messages.
/// </summary>
public abstract class WebsocketTransport : ReadLoopTransportBase
{
    /// <summary>
    /// Max time to wait for the peer to complete the close handshake on disconnect.
    /// After this timeout the read loop is cancelled, which may cause an RST.
    /// </summary>
    protected virtual int CloseHandshakeTimeoutMs => 1000;

    /// <summary>
    /// Web socket used to read and write messages.
    /// </summary>
    protected abstract WebSocket WebSocket { get; }

    // the socket captured by the running read loop, so that the loop's finally
    // block disposes the very socket it was started for, even if the property
    // has already been replaced by a reconnect
    private WebSocket _loopWebSocket;

    /// <summary>
    /// Completes the close handshake with the peer, then awaits the read loop.
    /// The loop itself closes the underlying socket in its finally block.
    /// </summary>
    public override async Task DisconnectAsync()
    {
        var ws = WebSocket;
        if (ws != null)
            await TrySendCloseFrame(ws, "Ok");

        var detachTask = DetachReadLoopAsync();

        // give the loop a chance to complete the close handshake with the peer
        var completed = await Task.WhenAny(detachTask,
            Task.Delay(CloseHandshakeTimeoutMs))
                .ConfigureAwait(false);

        if (completed != detachTask)
        {
            // peer did not complete the handshake in time, force cancel
            CancelReadLoop();
            await detachTask.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads incoming websocket messages and fires the
    /// <see cref="MessageTransportBase.ReceiveMessage"/> event.
    /// Responds to close frames to complete the close handshake.
    /// </summary>
    /// <param name="ct">Cancellation token of the read loop</param>
    protected override async Task ReceiveLoopBodyAsync(CancellationToken ct)
    {
        var buffer = new byte[BufferSize];
        var segment = new ArraySegment<byte>(buffer);
        var webSocket = WebSocket;

        _loopWebSocket = webSocket;

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
                await TrySendCloseFrame(webSocket);
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

            OnReceiveMessage(message);
        }
    }

    /// <summary>
    /// Disposes the socket of the ended loop. The connection state is managed
    /// by the client channel, so the base connected flag is intentionally
    /// not touched here.
    /// </summary>
    /// <param name="instance">Loop instance identity</param>
    protected override Task OnReadLoopEndedAsync(int instance)
    {
        _loopWebSocket?.TryDispose();
        _loopWebSocket = null;

        // the loop is the single fire point
        if (IsCurrentReadLoop(instance))
            OnDisconnected();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Not used: a websocket message spans multiple frames and is assembled
    /// in the <see cref="ReceiveLoopBodyAsync"/> override.
    /// </summary>
    /// <param name="ct">Cancellation token of the read loop</param>
    protected override Task<byte[]> ReadNextAsync(CancellationToken ct) =>
        Task.FromResult<byte[]>(null);

    private async Task TrySendCloseFrame(WebSocket webSocket, string status = "")
    {
        try
        {
            if (webSocket.State == WebSocketState.CloseReceived || webSocket.State == WebSocketState.Open)
                using (await SendLock)
                    await webSocket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure, status,
                            CancellationToken.None)
                                .ConfigureAwait(false);
        }
        catch
        {
            // peer may already be gone
        }
    }

    /// <inheritdoc />
    protected override async Task<bool> SendCoreAsync(byte[] rawMessage)
    {
        var ws = WebSocket;
        if (ws == null || ws.State != WebSocketState.Open)
        {
            LastException = new NetworkException("WebSocket is not connected, cannot send.");
            return false;
        }

        using (await SendLock)
            await ws.SendAsync(
                new ArraySegment<byte>(rawMessage),
                    WebSocketMessageType.Binary, true, CancellationToken.None)
                        .ConfigureAwait(false);

        return true;
    }
}

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
    /// Starts listening for the incoming messages.
    /// </summary>
    public virtual async Task<Guid> StartListening()
    {
        var oldLoop = _readLoop;
        var oldCts = Interlocked.Exchange(ref _readCts, new CancellationTokenSource());
        oldCts?.Cancel();
        oldCts?.Dispose();

        if (oldLoop != null)
            await oldLoop.ConfigureAwait(false);

        _disconnectFired.Reset();
        IsConnected = true;
        _readLoop = ReadIncomingMessages(_readCts.Token);

        return Guid.Empty;
    }

    /// <inheritdoc/>
    public virtual async ValueTask DisposeAsync()
    {
        await DisconnectAsync()
            .ConfigureAwait(false);

        var cts = Interlocked.Exchange(ref _readCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }

    /// <summary>
    /// Disconnects from the remote endpoint.
    /// </summary>
    public virtual async Task DisconnectAsync()
    {
        var wasConnected = IsConnected;
        IsConnected = false;

        _readCts?.Cancel();

        var loop = _readLoop;
        _readLoop = null;

        if (loop != null)
            await loop.ConfigureAwait(false);

        if (wasConnected)
        {
            try
            {
                SendMessage(ThisEndpoint, RemoteEndpoint, null, []);
            }
            catch
            {
                // peer may be gone
            }
        }
    }

    /// <summary>
    /// Reads the incoming websocket messages
    /// and fires the <see cref="ReceiveMessage"/> event.
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
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested)
                return;

            LastException = ex as NetworkException ??
                new NetworkException(ex.Message, ex);

            OnErrorOccured(ex.Message, LastException);
        }
        finally
        {
            IsConnected = false;

            // fire exactly once, even if DisconnectAsync is called afterwards
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

            ErrorOccured?.Invoke(ex.Message, LastException);
            return Task.FromResult(false);
        }
    }
}

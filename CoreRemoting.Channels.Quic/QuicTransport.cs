using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Quic;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Threading;
using CoreRemoting.Toolbox;

namespace CoreRemoting.Channels.Quic;

/// <summary>
/// Abstract QUIC transport for reading and writing messages, based on System.Net.Quic.
/// </summary>
public abstract class QuicTransport : IAsyncDisposable
{
    internal const string ProtocolName = nameof(CoreRemoting);

    /// <summary>
    /// Gets or sets the maximal size of network message.
    /// </summary>
    public int MaxMessageSize { get; set; } = 1024 * 1024 * 128;

    /// <summary>
    /// Quic protocol errors treated as normal shutdown, not failures.
    /// </summary>
    internal static readonly HashSet<QuicError> ShutdownSignals = new()
    {
        QuicError.ConnectionAborted,
        QuicError.StreamAborted,
        QuicError.OperationAborted,
        QuicError.ConnectionRefused
    };

    /// <inheritdoc />
    public NetworkException LastException { get; set; }

    /// <summary>
    /// Gets a value indicating whether the channel is connected.
    /// </summary>
    public bool IsConnected { get; protected set; }

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
    /// Quic connection used to read and write messages.
    /// </summary>
    protected QuicConnection Connection { get; set; }

    /// <summary>
    /// Quic stream used to read and write messages.
    /// </summary>
    protected QuicStream ClientStream { get; set; }

    protected AsyncLock ReceiveLock { get; } = new();

    protected AsyncLock SendLock { get; } = new();

    private Task _readLoop;
    private CancellationTokenSource _readLoopCts;
    private int _readLoopInstance;

    /// <summary>
    /// Starts listening for the incoming messages.
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

        _readLoop = ReadIncomingMessages(instance, _readLoopCts.Token);

        return Guid.Empty;
    }

    /// <inheritdoc />
    public async Task<bool> SendMessageAsync(byte[] rawMessage)
    {
        try
        {
            if (rawMessage.Length > MaxMessageSize)
                throw new InvalidOperationException("Message is too large. Max size: " +
                    MaxMessageSize + ", actual size: " + rawMessage.Length);

            using var sendLock = await SendLock;

            var lengthBuffer = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, rawMessage.Length);

            await ClientStream.WriteAsync(lengthBuffer, 0, 4).ConfigureAwait(false);
            await ClientStream.WriteAsync(rawMessage, 0, rawMessage.Length).ConfigureAwait(false);
            await ClientStream.FlushAsync().ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            // treat ConnectionAborted and other abort-related error signal as normal shutdown
            if (ex is QuicException qx && ShutdownSignals.Contains(qx.QuicError))
                return false;

            LastException = ex as NetworkException ??
                new NetworkException(ex.Message, ex);

            OnErrorOccured(ex.Message, ex);
            return false;
        }
    }

    /// <summary>
    /// Reads the incoming network messages until disconnected.
    /// </summary>
    protected async Task ReadIncomingMessages(int instance, CancellationToken ct)
    {
        var connection = Connection;
        var stream = ClientStream;

        try
        {
            while (!ct.IsCancellationRequested && IsConnected)
            {
                var message = await ReadIncomingMessage(stream, ct)
                    .ConfigureAwait(false);

                OnReceiveMessage(message);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // expected on explicit disconnect
        }
        catch (Exception ex)
        {
            // treat ConnectionAborted and other abort-related error signal as normal shutdown
            if (ex is QuicException qx && ShutdownSignals.Contains(qx.QuicError))
                return;

            LastException = ex as NetworkException ??
                new NetworkException(ex.Message, ex);

            OnErrorOccured(ex.Message, LastException);
        }
        finally
        {
            await connection.TryDisposeAsync().ConfigureAwait(false);
            if (ReferenceEquals(Connection, connection))
                Connection = null;

            await stream.TryDisposeAsync().ConfigureAwait(false);
            if (ReferenceEquals(ClientStream, stream))
                ClientStream = null;

            if (Volatile.Read(ref _readLoopInstance) == instance)
            {
                IsConnected = false;
                OnDisconnected();
            }
        }
    }

    protected async Task<byte[]> ReadIncomingMessage(Stream stream, CancellationToken ct)
    {
        using var receiveLock = await ReceiveLock;

        var lengthBuffer = new byte[4];
        await stream.ReadExactlyAsync(lengthBuffer, ct)
            .ConfigureAwait(false);

        var messageSize = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);

        if (messageSize < 0 || messageSize > MaxMessageSize)
            throw new InvalidOperationException($"Invalid message size: {messageSize}");

        var messageBuffer = new byte[messageSize];
        await stream.ReadExactlyAsync(messageBuffer, ct)
            .ConfigureAwait(false);

        return messageBuffer;
    }

    /// <inheritdoc />
    public virtual async Task DisconnectAsync()
    {
        try
        {
            if (IsConnected && Connection != null)
                await Connection.CloseAsync(0x0C)
                    .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LastException = ex as NetworkException ??
                new NetworkException(ex.Message, ex);

            OnErrorOccured(ex.Message, LastException);
        }

        await DetachReadLoopAsync()
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Atomically detaches the current read loop and awaits its completion.
    /// </summary>
    private async Task DetachReadLoopAsync()
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

    /// <inheritdoc/>
    public virtual async ValueTask DisposeAsync()
    {
        await DisconnectAsync()
            .ConfigureAwait(false);

        using var cts = Interlocked.Exchange(ref _readLoopCts, null);
        cts?.Cancel();

        ReceiveLock.TryDispose();
        SendLock.TryDispose();
    }
}
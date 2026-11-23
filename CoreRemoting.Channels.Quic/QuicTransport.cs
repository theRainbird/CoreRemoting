using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Quic;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Channels.Common;
using CoreRemoting.Threading;
using CoreRemoting.Toolbox;

namespace CoreRemoting.Channels.Quic;

/// <summary>
/// Abstract QUIC transport for reading and writing messages, based on System.Net.Quic.
/// </summary>
public abstract class QuicTransport : ReadLoopTransportBase
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

    /// <summary>
    /// Quic connection used to read and write messages.
    /// </summary>
    protected QuicConnection Connection { get; set; }

    /// <summary>
    /// Quic stream used to read and write messages.
    /// </summary>
    protected QuicStream ClientStream { get; set; }

    // captured at loop start so the teardown disposes the objects this loop was
    // created for, even if the properties were already replaced by a reconnect
    private QuicConnection _loopConnection;
    private QuicStream _loopStream;

    /// <summary>
    /// Captures the current connection and stream for this read loop.
    /// </summary>
    protected override void OnLoopStarting()
    {
        _loopConnection = Connection;
        _loopStream = ClientStream;
    }

    /// <inheritdoc />
    /// <remarks>
    /// QUIC abort-related errors are treated as normal shutdown and don't fire
    /// the <see cref="MessageTransportBase.ErrorOccured"/> event.
    /// </remarks>
    protected override bool IsNormalShutdown(Exception ex) =>
        ex is QuicException qx && ShutdownSignals.Contains(qx.QuicError);

    /// <inheritdoc />
    protected override async Task<bool> SendCoreAsync(byte[] rawMessage)
    {
        if (rawMessage.Length > MaxMessageSize)
            throw new InvalidOperationException("Message is too large. Max size: " +
                MaxMessageSize + ", actual size: " + rawMessage.Length);

        using var sendLock = await SendLock;

        await LengthPrefixedFrame
            .WriteFrameAsync(ClientStream, rawMessage, CancellationToken.None)
            .ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Preserves the original behavior of reporting the raw exception
    /// (not the wrapped <see cref="MessageTransportBase.LastException"/>)
    /// for failed send operations.
    /// </remarks>
    protected override void ReportSendError(Exception ex)
    {
        LastException = ex as NetworkException ??
            new NetworkException(ex.Message, ex);

        OnErrorOccured(ex.Message, ex);
    }

    /// <inheritdoc />
    protected override async Task<byte[]> ReadNextAsync(CancellationToken ct) =>
        await ReadIncomingMessage(_loopStream, ct)
            .ConfigureAwait(false);

    /// <summary>
    /// Reads one length prefixed message from the given stream.
    /// </summary>
    /// <param name="stream">Stream to read from</param>
    /// <param name="ct">Cancellation token</param>
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
    /// <remarks>
    /// Disposes the connection and stream captured by this read loop and then
    /// applies the base disconnect semantics.
    /// </remarks>
    protected override async Task OnReadLoopEndedAsync(int instance)
    {
        if (_loopConnection != null)
        {
            await _loopConnection.TryDisposeAsync().ConfigureAwait(false);
            if (ReferenceEquals(Connection, _loopConnection))
                Connection = null;
        }

        if (_loopStream != null)
        {
            await _loopStream.TryDisposeAsync().ConfigureAwait(false);
            if (ReferenceEquals(ClientStream, _loopStream))
                ClientStream = null;
        }

        await base.OnReadLoopEndedAsync(instance)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task DisconnectAsync()
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

        // safety net: if the close above failed or was skipped, the loop would
        // otherwise stay blocked on the stream read and never detach
        CancelReadLoop();

        await DetachReadLoopAsync()
            .ConfigureAwait(false);
    }
}

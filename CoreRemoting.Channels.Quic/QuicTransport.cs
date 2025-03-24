using System;
using System.IO;
using System.Net.Quic;
using System.Threading.Tasks;
using CoreRemoting.Threading;

namespace CoreRemoting.Channels.Quic;

/// <summary>
/// Abstract QUIC transport for reading and writing messages, based on System.Net.Quic.
/// </summary>
public abstract class QuicTransport : IAsyncDisposable
{
    internal const int MaxMessageSize = 1024 * 1024 * 128;
    internal const string ProtocolName = nameof(CoreRemoting);

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

    /// <summary>
    /// Quic reader used to read messages.
    /// </summary>
    protected BinaryReader ClientReader { get; set; }

    /// <summary>
    /// Quic writer used to write messages.
    /// </summary>
    protected BinaryWriter ClientWriter { get; set; }

    protected AsyncLock ReceiveLock { get; } = new();

    protected AsyncLock SendLock { get; } = new();

    /// <inheritdoc/>
    public virtual ValueTask DisposeAsync()
    {
        ReceiveLock.Dispose();
        SendLock.Dispose();
        return default;
    }
}

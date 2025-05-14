using System;
using System.Threading.Tasks;
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
    public virtual Guid StartListening()
    {
        IsConnected = true;
        _ = ReadIncomingMessages();
        return Guid.Empty;
    }

    /// <inheritdoc/>
    public virtual ValueTask DisposeAsync() => default;

    /// <summary>
    /// Disconnects from the remote endpoint.
    /// </summary>
    public virtual Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads the incoming websocket messages
    /// and fires the <see cref="ReceiveMessage"/> event.
    /// </summary>
    private async Task ReadIncomingMessages()
    {
        try
        {
            while (IsConnected)
            {
                await foreach (var message in
                    ReceiveMessagesAsync(null, RemoteEndpoint, ThisEndpoint)
                        .ConfigureAwait(false))
                {
                    OnReceiveMessage(message.Message ?? []);
                }
            }
        }
        catch (Exception ex)
        {
            LastException = ex as NetworkException ??
                new NetworkException(ex.Message, ex);

            OnErrorOccured(ex.Message, LastException);
        }
        finally
        {
            await DisconnectAsync()
                .ConfigureAwait(false);
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

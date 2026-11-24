using System;
using System.Threading.Tasks;

namespace CoreRemoting.Channels.Common;

/// <summary>
/// Base class for client channels that own a raw message transport via composition.
/// Provides the common connect/disconnect lifecycle skeleton:
/// open transport, start listening, send handshake, signal connection.
/// </summary>
/// <typeparam name="TTransport">Concrete transport type used by this channel</typeparam>
public abstract class ClientChannelBase<TTransport> : IClientChannel
    where TTransport : MessageTransportBase
{
    /// <summary>
    /// The remoting client that owns this channel.
    /// </summary>
    protected IRemotingClient Client { get; private set; }

    /// <summary>
    /// The raw message transport owned by this channel.
    /// </summary>
    protected TTransport Transport { get; private set; }

    /// <inheritdoc />
    public IRawMessageTransport RawMessageTransport => Transport;

    /// <inheritdoc />
    public abstract bool IsConnected { get; }

    /// <inheritdoc />
    public event Action Disconnected;

    /// <inheritdoc />
    public virtual void Init(IRemotingClient client)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));

        Transport = CreateTransport(client);

        // forward the transport's disconnect to the channel's subscribers
        Transport.Disconnected += OnTransportDisconnected;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Template: opens the transport, starts listening for incoming messages,
    /// sends the client handshake and signals the established connection.
    /// </remarks>
    public virtual async Task ConnectAsync()
    {
        if (IsConnected)
            return;

        await OpenTransportAsync()
            .ConfigureAwait(false);

        await StartListeningAsync()
            .ConfigureAwait(false);

        await SendHandshakeAsync()
            .ConfigureAwait(false);

        OnConnected();
    }

    /// <inheritdoc />
    public abstract Task DisconnectAsync();

    /// <inheritdoc />
    public abstract ValueTask DisposeAsync();

    /// <summary>
    /// Creates the transport instance for this channel. Called once from
    /// <see cref="Init"/>. Must not open any connection yet.
    /// </summary>
    /// <param name="client">The remoting client</param>
    protected abstract TTransport CreateTransport(IRemotingClient client);

    /// <summary>
    /// Opens the underlying connection of the transport.
    /// </summary>
    protected abstract Task OpenTransportAsync();

    /// <summary>
    /// Starts listening for incoming messages.
    /// No-op for event-driven transports that receive via library events.
    /// </summary>
    protected virtual Task StartListeningAsync() =>
        Task.CompletedTask;

    /// <summary>
    /// Sends the client handshake message. The default implementation
    /// serializes the client's handshake message and sends it via the transport.
    /// </summary>
    protected virtual async Task SendHandshakeAsync()
    {
        var bytes = Client.Serializer.Serialize(Client.HandshakeMessage);

        await Transport.SendMessageAsync(bytes)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Signals that the connection has been established by raising the
    /// transport's <see cref="MessageTransportBase.Connected"/> event.
    /// </summary>
    protected virtual void OnConnected() =>
        Transport.RaiseConnected();

    /// <summary>
    /// Forwards the transport's disconnect event to the channel's subscribers.
    /// </summary>
    protected virtual void OnTransportDisconnected() =>
        Disconnected?.Invoke();
}

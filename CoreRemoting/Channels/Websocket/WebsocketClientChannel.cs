using System.Threading.Tasks;
using CoreRemoting.Channels.Common;

namespace CoreRemoting.Channels.Websocket;

/// <summary>
/// Client side Websocket channel implementation.
/// </summary>
public class WebsocketClientChannel : ClientChannelBase<WebsocketClientTransport>
{
    /// <inheritdoc />
    public override bool IsConnected => Transport.IsConnected;

    /// <inheritdoc />
    protected override WebsocketClientTransport CreateTransport(IRemotingClient client) =>
        new(client);

    /// <inheritdoc />
    protected override Task OpenTransportAsync() =>
        Transport.OpenAsync();

    /// <inheritdoc />
    protected override Task StartListeningAsync() =>
        Transport.StartListening();

    /// <inheritdoc />
    /// <remarks>
    /// The websocket handshake is an empty message; the handshake metadata
    /// is carried as cookies established during <see cref="OpenTransportAsync"/>.
    /// </remarks>
    protected override Task SendHandshakeAsync() =>
        Transport.SendMessageAsync([]);

    /// <inheritdoc />
    public override Task DisconnectAsync() =>
        Transport.DisconnectAsync();

    /// <inheritdoc />
    public override ValueTask DisposeAsync() =>
        Transport.DisposeAsync();
}

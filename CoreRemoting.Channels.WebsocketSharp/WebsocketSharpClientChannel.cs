using System;
using System.Threading.Tasks;
using CoreRemoting.Channels.Common;

namespace CoreRemoting.Channels.WebsocketSharp;

/// <summary>
/// Client side websocket channel implementation based on websocket-sharp.
/// </summary>
public class WebsocketSharpClientChannel : ClientChannelBase<WebsocketSharpClientTransport>
{
    /// <inheritdoc />
    /// <remarks>
    /// Throws when the channel is not initialized, like the original implementation.
    /// </remarks>
    public override bool IsConnected =>
        Transport?.IsConnected ?? throw new InvalidOperationException("Channel is not initialized.");

    /// <inheritdoc />
    protected override WebsocketSharpClientTransport CreateTransport(IRemotingClient client) =>
        new(client);

    /// <inheritdoc />
    protected override Task OpenTransportAsync()
    {
        if (Transport == null)
            throw new InvalidOperationException("Channel is not initialized.");

        Transport.Open();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The websocket-sharp handshake is an empty string message; the handshake
    /// metadata is carried as cookies established during transport creation.
    /// The disconnect handler is attached only after the handshake was sent,
    /// so a late close event of a previous connection cannot reach the client
    /// while the new connection is being set up.
    /// </remarks>
    protected override Task SendHandshakeAsync()
    {
        Transport.SendHandshake();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task DisconnectAsync() =>
        Transport?.DisconnectAsync() ?? Task.CompletedTask;

    /// <inheritdoc />
    public override ValueTask DisposeAsync() =>
        Transport?.DisposeAsync() ?? default;
}

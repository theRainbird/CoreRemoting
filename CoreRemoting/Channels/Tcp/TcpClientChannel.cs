using System;
using System.Threading.Tasks;
using CoreRemoting.Channels.Common;

namespace CoreRemoting.Channels.Tcp;

/// <summary>
/// Client side TCP channel implementation.
/// </summary>
public class TcpClientChannel : ClientChannelBase<TcpClientTransport>
{
    /// <inheritdoc />
    public override bool IsConnected =>
        Transport?.IsConnected == true;

    /// <inheritdoc />
    protected override TcpClientTransport CreateTransport(IRemotingClient client) =>
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
    /// The handshake is sent as a zero byte message with the handshake
    /// metadata attached as WatsonTcp header. The disconnect handler is
    /// attached only after the handshake completed, so a late teardown
    /// event of a previous connection cannot disturb the new one.
    /// </remarks>
    protected override async Task SendHandshakeAsync()
    {
        await Transport.SendHandshakeAsync()
            .ConfigureAwait(false);

        Transport.AttachDisconnectHandler();
    }

    /// <inheritdoc />
    public override Task DisconnectAsync() =>
        Transport?.DisconnectAsync() ?? Task.CompletedTask;

    /// <inheritdoc />
    public override ValueTask DisposeAsync() =>
        Transport?.DisposeAsync() ?? default;
}

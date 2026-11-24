using System.Threading.Tasks;
using CoreRemoting.Channels.Common;

namespace CoreRemoting.Channels.Null;

/// <summary>
/// Simple in-process channel, client-side.
/// </summary>
public class NullClientChannel : ClientChannelBase<NullClientTransport>
{
    /// <inheritdoc />
    public override bool IsConnected => Transport.IsConnected;

    /// <inheritdoc />
    protected override NullClientTransport CreateTransport(IRemotingClient client) =>
        new(client);

    /// <inheritdoc />
    protected override Task OpenTransportAsync() =>
        Transport.OpenAsync();

    /// <inheritdoc />
    protected override Task StartListeningAsync() =>
        Transport.StartListening();

    /// <inheritdoc />
    /// <remarks>
    /// The Null channel carries the handshake metadata already with the
    /// queue connection, so no explicit handshake message is sent.
    /// </remarks>
    protected override Task SendHandshakeAsync() =>
        Task.CompletedTask;

    /// <inheritdoc />
    public override Task DisconnectAsync() =>
        Transport.DisconnectAsync();

    /// <inheritdoc />
    public override ValueTask DisposeAsync() =>
        Transport.DisposeAsync();
}

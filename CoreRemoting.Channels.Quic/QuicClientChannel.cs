using System.Threading.Tasks;
using CoreRemoting.Channels.Common;

namespace CoreRemoting.Channels.Quic;

/// <summary>
/// QUIC client channel, based on System.Net.Quic.
/// </summary>
public class QuicClientChannel : ClientChannelBase<QuicClientTransport>
{
    /// <inheritdoc />
    public override bool IsConnected => Transport.IsConnected;

    /// <inheritdoc />
    protected override QuicClientTransport CreateTransport(IRemotingClient client) =>
        new(client);

    /// <inheritdoc />
    protected override Task OpenTransportAsync() =>
        Transport.OpenAsync();

    /// <inheritdoc />
    protected override Task StartListeningAsync() =>
        Transport.StartListening();

    /// <inheritdoc />
    /// <remarks>
    /// The QUIC handshake is the serialized client handshake message,
    /// sent as the first length prefixed frame on the client stream.
    /// </remarks>
    protected override async Task SendHandshakeAsync()
    {
        var handshakeMessage = Client.HandshakeMessage;
        var handshakeBytes = Client.Serializer.Serialize(handshakeMessage);

        await Transport.SendMessageAsync(handshakeBytes)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Resets the transport's connection state if a step after the connection
    /// was established failed, so the channel is not reported as connected.
    /// </remarks>
    public override async Task ConnectAsync()
    {
        try
        {
            await base.ConnectAsync()
                .ConfigureAwait(false);
        }
        catch
        {
            Transport.MarkFailed();
            throw;
        }
    }

    /// <inheritdoc />
    public override Task DisconnectAsync() =>
        Transport.DisconnectAsync();

    /// <inheritdoc />
    public override ValueTask DisposeAsync() =>
        Transport.DisposeAsync();
}

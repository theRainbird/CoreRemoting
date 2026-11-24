using System;
using System.Threading.Tasks;
using CoreRemoting.Channels.Common;

namespace CoreRemoting.Channels.NamedPipe;

/// <summary>
/// Client side Named Pipe channel implementation.
/// </summary>
public class NamedPipeClientChannel : ClientChannelBase<NamedPipeClientTransport>
{
	/// <summary>
	/// Tracing handler. Must be set before initialization to take effect.
	/// </summary>
	public Action<string> TraceWriteLine { get; set; }

	/// <inheritdoc />
	/// <remarks>
	/// Returns false before initialization, like the original implementation.
	/// </remarks>
	public override bool IsConnected =>
		Transport?.IsConnected == true;

	/// <inheritdoc />
	protected override NamedPipeClientTransport CreateTransport(IRemotingClient client) =>
		new(client)
		{
			TraceWriteLine = TraceWriteLine
		};

	/// <inheritdoc />
	protected override Task OpenTransportAsync() =>
		Transport?.OpenAsync() ?? throw new InvalidOperationException("Channel is not initialized.");

	/// <inheritdoc />
	protected override Task StartListeningAsync() =>
		Transport.StartListening();

	/// <inheritdoc />
	protected override Task SendHandshakeAsync() =>
		Transport.SendHandshakeAsync();

	/// <inheritdoc />
	public override Task DisconnectAsync() =>
		Transport.DisconnectAsync();

	/// <inheritdoc />
	public override ValueTask DisposeAsync() =>
		Transport.DisposeAsync();
}
